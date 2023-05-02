using System;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using ReactiveUI;

namespace MemoriaNote
{
    public class Metadata : ReactiveObject, IDataSource
    {
        public Metadata() { }
        public Metadata(string dataSource)
        {
            DataSource = dataSource;
        }

        string _name = null;
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

        public string DataSource { get; set; }

        public void CopyTo(IDataSource dest) => DataSourceTracker.Create(DataSource).CopyTo(dest);
        public override string ToString() => DataSourceTracker.Create(DataSource).ToString();
        public IDataSource Clone() => DataSourceTracker.Create(DataSource);
        public override int GetHashCode() => DataSourceTracker.Create(DataSource).GetHashCode();
        public bool Equals(IDataSource other) => this.GetHashCode() == other.GetHashCode();
    }

    public class NoteKeyValue
    {
        [Key]
        public string Key { get; set; }
        public string Value { get; set; }

        public static string Get(NoteDbContext context, string key)
        {
            if (context == null)
                return null;

            var entity = context.Metadata.Find(key);
            if (entity == null) return null;
            else
                return entity.Value;
        }

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
