using System;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using ReactiveUI;

namespace MemoriaNote
{
    /// <summary>
    /// Represents metadata associated with a data source. Inherits from ReactiveObject and implements IDataSource.
    /// </summary>
    public class Metadata : ReactiveObject, IDataSource
    {
        public Metadata() { }
        public Metadata(string dataSource)
        {
            DataSource = dataSource;
        }

        string _name = null;
        /// <summary>
        /// Gets or sets the Name property. Retrieves the value from the database if not set.
        /// </summary>
        public string Name
        {
            get
            {
                if (_name == null)
                {
                    using (NoteDbContext db = new NoteDbContext(DataSource))
                        _name = NoteKeyValue.Get(db, NoteKeyValue.Name);
                }
                return _name;
            }
            set
            {
                NoteDbContext db = null;
                try
                {
                    if (_name == null)
                    {
                        db = new NoteDbContext(DataSource);
                        _name = NoteKeyValue.Get(db, NoteKeyValue.Name);
                    }

                    if (_name != value)
                    {
                        this.RaiseAndSetIfChanged(ref _name, value);
                        if (db == null)
                            db = new NoteDbContext(DataSource);
                        NoteKeyValue.Set(db, NoteKeyValue.Name, _name);
                    }
                }
                finally
                {
                    if (db != null)
                    {
                        db.Dispose();
                        db = null;
                    }
                }
            }
        }

        string _title = null;
        /// <summary>
        /// Gets or sets the Title property. Retrieves the value from the database if not set.
        /// </summary>
        public string Title
        {
            get
            {
                if (_title == null)
                {
                    using (NoteDbContext db = new NoteDbContext(DataSource))
                        _title = NoteKeyValue.Get(db, NoteKeyValue.Title);
                }
                return _title;
            }
            set
            {
                NoteDbContext db = null;
                try
                {
                    if (_title == null)
                    {
                        db = new NoteDbContext(DataSource);
                        _title = NoteKeyValue.Get(db, NoteKeyValue.Title);
                    }

                    if (_title != value)
                    {
                        this.RaiseAndSetIfChanged(ref _title, value);
                        if (db == null)
                            db = new NoteDbContext(DataSource);
                        NoteKeyValue.Set(db, NoteKeyValue.Title, _title);
                    }
                }
                finally
                {
                    if (db != null)
                    {
                        db.Dispose();
                        db = null;
                    }
                }
            }
        }

        string _version = null;
        /// <summary>
        /// Gets or sets the Version property. Retrieves the value from the database if not set.
        /// </summary>
        public string Version
        {
            get
            {
                if (_version == null)
                {
                    using (NoteDbContext db = new NoteDbContext(DataSource))
                        _version = NoteKeyValue.Get(db, NoteKeyValue.Version);
                }
                return _version;
            }
            set
            {
                NoteDbContext db = null;
                try
                {
                    if (_version == null)
                    {
                        db = new NoteDbContext(DataSource);
                        _version = NoteKeyValue.Get(db, NoteKeyValue.Version);
                    }

                    if (_version != value)
                    {
                        this.RaiseAndSetIfChanged(ref _version, value);
                        if (db == null)
                            db = new NoteDbContext(DataSource);
                        NoteKeyValue.Set(db, NoteKeyValue.Version, _version);
                    }
                }
                finally
                {
                    if (db != null)
                    {
                        db.Dispose();
                        db = null;
                    }
                }
            }
        }

        string _description = null;
        /// <summary>
        /// Gets or sets the Description property. Retrieves the value from the database if not set.
        /// </summary>
        public string Description
        {
            get
            {
                if (_description == null)
                {
                    using (NoteDbContext db = new NoteDbContext(DataSource))
                        _description = NoteKeyValue.Get(db, NoteKeyValue.Description);
                }
                return _description;
            }
            set
            {
                NoteDbContext db = null;
                try
                {
                    if (_description == null)
                    {
                        db = new NoteDbContext(DataSource);
                        _description = NoteKeyValue.Get(db, NoteKeyValue.Description);
                    }
                    if (_description != value)
                    {
                        this.RaiseAndSetIfChanged(ref _description, value);
                        if (db == null)
                            db = new NoteDbContext(DataSource);
                        NoteKeyValue.Set(db, NoteKeyValue.Description, _description);
                    }
                }
                finally
                {
                    if (db != null)
                    {
                        db.Dispose();
                        db = null;
                    }
                }
            }
        }

        string _author = null;
        /// <summary>
        /// Gets or sets the Author property. Retrieves the value from the database if not set.
        /// </summary>
        /// <remarks>
        /// The Author property represents the name of the author associated with the metadata.
        /// If the author value is not set, it is retrieved from the database using the NoteDbContext.
        /// The value can be set and saved to the database using the NoteKeyValue class.
        /// </remarks>
        public string Author
        {
            get
            {
                if (_author == null)
                    using (NoteDbContext db = new NoteDbContext(DataSource))
                        _author = NoteKeyValue.Get(db, NoteKeyValue.Author);

                return _author;
            }
            set
            {
                NoteDbContext db = null;
                try
                {
                    if (_author == null)
                    {
                        db = new NoteDbContext(DataSource);
                        _author = NoteKeyValue.Get(db, NoteKeyValue.Author);
                    }
                    if (_author != value)
                    {
                        this.RaiseAndSetIfChanged(ref _author, value);
                        if (db == null)
                            db = new NoteDbContext(DataSource);
                        NoteKeyValue.Set(db, NoteKeyValue.Author, _author);
                    }
                }
                finally
                {
                    if (db != null)
                    {
                        db.Dispose();
                        db = null;
                    }
                }
            }
        }

        /// <summary>
        /// Gets or sets the ReadOnly property. Retrieves the value from the database as a string, converts it to a boolean value, and returns it.
        /// If no value is found in the database, it defaults to "false".
        /// The value can be set and saved to the database as a string representation of a boolean value.
        /// </summary>
        public bool ReadOnly
        {
            get
            {
                var text = ReadOnlyAsString ?? "false";
                return bool.Parse(text);
            }
            set => ReadOnlyAsString = value.ToString();
        }
        string _readOnly = null;
        /// <summary>
        /// This property represents the string value for the ReadOnly property. It retrieves the value from the database using the NoteDbContext if it is not set.
        /// The value can be set and saved to the database using the NoteKeyValue class.
        /// </summary>
        protected string ReadOnlyAsString
        {
            get
            {
                if (_readOnly == null)
                {
                    using (NoteDbContext db = new NoteDbContext(DataSource))
                        _readOnly = NoteKeyValue.Get(db, NoteKeyValue.ReadOnly);
                }
                return _readOnly;
            }
            set
            {
                NoteDbContext db = null;
                try
                {
                    if (_readOnly == null)
                    {
                        db = new NoteDbContext(DataSource);
                        _readOnly = NoteKeyValue.Get(db, NoteKeyValue.ReadOnly);
                    }
                    if (_readOnly != value)
                    {
                        this.RaiseAndSetIfChanged(ref _readOnly, value);
                        if (db == null)
                            db = new NoteDbContext(DataSource);
                        NoteKeyValue.Set(db, NoteKeyValue.ReadOnly, _readOnly);
                    }
                }
                finally
                {
                    if (db != null)
                    {
                        db.Dispose();
                        db = null;
                    }
                }
            }
        }

        string _tag = null;
        /// <summary>
        /// Gets or sets the Tag property. Retrieves the value from the database if not set.
        /// </summary>
        /// <remarks>
        /// The Tag property represents a tag associated with the metadata.
        /// If the tag value is not set, it is retrieved from the database using the NoteDbContext.
        /// The value can be set and saved to the database using the NoteKeyValue class.
        /// </remarks>
        public string Tag
        {
            get
            {
                if (_tag == null)
                {
                    using (NoteDbContext db = new NoteDbContext(DataSource))
                        _tag = NoteKeyValue.Get(db, NoteKeyValue.Tag);
                }
                return _tag;
            }
            set
            {
                NoteDbContext db = null;
                try
                {
                    if (_tag == null)
                    {
                        db = new NoteDbContext(DataSource);
                        _tag = NoteKeyValue.Get(db, NoteKeyValue.Tag);
                    }
                    if (_tag != value)
                    {
                        this.RaiseAndSetIfChanged(ref _tag, value);
                        if (db == null)
                            db = new NoteDbContext(DataSource);
                        NoteKeyValue.Set(db, NoteKeyValue.Tag, _tag);
                    }
                }
                finally
                {
                    if (db != null)
                    {
                        db.Dispose();
                        db = null;
                    }
                }
            }
        }

        /// <summary>
        /// Gets or sets the CreateTime property. Retrieves the value from the database as a string, converts it to a DateTime value, and returns it.
        /// If no value is found in the database, it defaults to the default DateTime value.
        /// The value can be set and saved to the database as a string representation of a DateTime value.
        /// </summary>
        public DateTime CreateTime
        {
            get
            {
                var createTime = CreateTimeAsString ?? default(DateTime).ToDateString();
                return DateTime.Parse(createTime);
            }
            set => CreateTimeAsString = value.ToDateString();
        }
        string _createTime = null;
        /// <summary>
        /// This property represents the string value for the CreateTime property. 
        /// It retrieves the value from the database using the NoteDbContext if it is not set.
        /// The value can be set and saved to the database using the NoteKeyValue class.
        /// </summary>
        protected string CreateTimeAsString
        {
            get
            {
                if (_createTime == null)
                {
                    using (NoteDbContext db = new NoteDbContext(DataSource))
                        _createTime = NoteKeyValue.Get(db, NoteKeyValue.CreateTime);
                }
                return _createTime;
            }
            set
            {
                NoteDbContext db = null;
                try
                {
                    if (_createTime == null)
                    {
                        db = new NoteDbContext(DataSource);
                        _createTime = NoteKeyValue.Get(db, NoteKeyValue.CreateTime);
                    }
                    if (_createTime != value)
                    {
                        this.RaiseAndSetIfChanged(ref _createTime, value);
                        if (db == null)
                            db = new NoteDbContext(DataSource);
                        NoteKeyValue.Set(db, NoteKeyValue.CreateTime, _createTime);
                    }
                }
                finally
                {
                    if (db != null)
                    {
                        db.Dispose();
                        db = null;
                    }
                }
            }
        }

        /// <summary>
        /// Gets or sets the DataSource property. Represents the data source associated with the metadata.
        /// The DataSource value can be set to specify the data source to be used for retrieving and storing metadata information.
        /// </summary>
        public string DataSource { get; set; }

        /// <summary>
        /// Copy the current data source to the specified destination data source.
        /// </summary>
        /// <param name="dest">The destination data source to copy to.</param>
        public void CopyTo(IDataSource dest) => DataSourceTracker.Create(DataSource).CopyTo(dest);

        /// <summary>
        /// Returns a string representation of the current data source.
        /// </summary>
        /// <returns>A string representation of the current data source.</returns>
        public override string ToString() => DataSourceTracker.Create(DataSource).ToString();

        /// <summary>
        /// Creates a clone of the current data source.
        /// </summary>
        /// <returns>A clone of the current data source.</returns>
        public IDataSource Clone() => DataSourceTracker.Create(DataSource);

        /// <summary>
        /// Returns a hash code for the current data source.
        /// </summary>
        /// <returns>A hash code for the current data source.</returns>
        public override int GetHashCode() => DataSourceTracker.Create(DataSource).GetHashCode();

        /// <summary>
        /// Determines whether the current data source is equal to another data source.
        /// </summary>
        /// <param name="other">The data source to compare with.</param>
        /// <returns>True if the current data source is equal to the other data source, otherwise false.</returns>
        public bool Equals(IDataSource other) => this.GetHashCode() == other.GetHashCode();
    }

    /// <summary>
    /// Represents a class that manages key-value pairs for storing and retrieving metadata in a NoteDbContext.
    /// </summary>
    public class NoteKeyValue
    {
        /// <summary>
        /// Gets or sets the Key property. Represents the key of the NoteKeyValue entity.
        /// The Key is used to uniquely identify a NoteKeyValue entity.
        /// </summary>
        [Key]
        public string Key { get; set; }

        /// <summary>
        /// Gets or sets the Value property. Represents the value associated with the Key in the NoteKeyValue entity.
        /// The Value holds the actual data or information related to the Key in the NoteKeyValue entity.
        /// </summary>
        public string Value { get; set; }

        /// <summary>
        /// Retrieves the value associated with the specified key from the NoteKeyValue entity in the given NoteDbContext.
        /// If the context is null or the entity for the key is not found, it returns null.
        /// </summary>
        /// <param name="context">The NoteDbContext where the data is stored.</param>
        /// <param name="key">The key to identify the value in the NoteKeyValue entity.</param>
        /// <returns>The value associated with the key if found; otherwise, null.</returns>
        public static string Get(NoteDbContext context, string key)
        {
            if (context == null)
                return null;

            var entity = context.Metadata.Find(key);
            if (entity == null) return null;
            else
                return entity.Value;
        }

        /// <summary>
        /// Sets the value associated with the specified key in the NoteKeyValue entity within the given NoteDbContext.
        /// If the context is null, the method returns without performing any action.
        /// If the entity for the key is not found, a new entity is added with the provided key and value.
        /// If the entity for the key already exists, the value is updated to the provided value.
        /// After updating or adding the entity, the changes are saved to the database using the SaveChanges method of the context.
        /// </summary>
        /// <param name="context">The NoteDbContext where the data is stored.</param>
        /// <param name="key">The key to identify the value in the NoteKeyValue entity.</param>
        /// <param name="value">The value to set or update for the specified key.</param>
        public static void Set(NoteDbContext context, string key, string value)
        {
            if (context == null)
                return;

            var entity = context.Metadata.Find(key);
            if (entity == null)
            {
                context.Metadata.Add(new NoteKeyValue() { Key = key, Value = value });
            }
            else
            {
                var current = new NoteKeyValue() { Key = key, Value = value };
                context.Entry(entity).CurrentValues.SetValues(current);
            }
            context.SaveChanges();
        }

        /// <summary>
        /// Asynchronously sets the value associated with the specified key in the NoteKeyValue entity within the given NoteDbContext using the provided data source.
        /// If the data source is null or empty, an ArgumentNullException is thrown.
        /// If the entity for the key is not found, a new entity is added with the provided key and value.
        /// If the entity for the key already exists, the value is updated to the provided value.
        /// After updating or adding the entity, the changes are saved to the database using the SaveChanges method of the context.
        /// </summary>
        /// <param name="dataSource">The data source to access the NoteDbContext for data management.</param>
        /// <param name="key">The key to identify the value in the NoteKeyValue entity.</param>
        /// <param name="value">The value to set or update for the specified key.</param>
        /// <returns>A Task representing the asynchronous operation.</returns>
        public static Task SetAsync(string dataSource, string key, string value)
        {
            if (string.IsNullOrEmpty(dataSource))
                throw new ArgumentNullException("dataSource");

            Task task = Task.Run(() =>
            {
                using (NoteDbContext conn = new NoteDbContext(dataSource))
                {
                    var entity = conn.Metadata.Find(key);
                    if (entity == null)
                    {
                        conn.Metadata.Add(new NoteKeyValue() { Key = key, Value = value });
                    }
                    else
                    {
                        var current = new NoteKeyValue() { Key = key, Value = value };
                        conn.Entry(entity).CurrentValues.SetValues(current);
                    }
                    conn.SaveChanges();
                }
            });
            return task;
        }

        public static string Name => nameof(Name);
        public static string Title => nameof(Title);
        public static string Version => nameof(Version);
        public static string ReadOnly => nameof(ReadOnly);
        public static string Description => nameof(Description);
        public static string Author => nameof(Author);
        public static string Tag => nameof(Tag);
        public static string CreateTime => nameof(CreateTime);
    }
}
