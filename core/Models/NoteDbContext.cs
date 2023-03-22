using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Extensions.Logging;
using Serilog.Extensions.Hosting;
using Serilog.AspNetCore;
using Serilog;

namespace MemoriaNote
{
    public class NoteDbContext : DbContext
    {
        public NoteDbContext() { }
        public NoteDbContext(string dataSource)
        {
            DataSource = dataSource;
        }

        public DbSet<NoteKeyValue> TitlePage { get; set; }

        public DbSet<Page> Pages { get; set; }

        public DbSet<Content> Contents { get; set; }

        public DbSet<PageHistory> History { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder
                .UseLoggerFactory(MyLoggerFactory)
                .UseSqlite("Data Source=" + (DataSource ?? ":memory:"));
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Page>()
                .HasKey(e => e.Rowid);

            modelBuilder.Entity<Page>()
                .Property(e => e.Rowid)
                .ValueGeneratedOnAdd();

            modelBuilder.Entity<Page>()
                .HasAlternateKey(e => e.GuidAsString);

            modelBuilder.Entity<Page>()
                .HasIndex(e => new { e.Title, e.Index });

            modelBuilder.Entity<Page>()
                .Property(e => e.Noteid)
                .IsRequired();                

            modelBuilder.Entity<Content>()
                .HasKey(e => e.GuidAsString);

            modelBuilder.Entity<Content>()
                .HasIndex(e => new { e.Title, e.Index });

            modelBuilder.Entity<Content>()
                .Property(e => e.Noteid)
                .IsRequired();

            modelBuilder.Entity<PageHistory>()
                .HasKey(e => new { e.Rowid, e.Generation });
        }

        ContentClient _contentClient = null;
        public ContentClient ContentClient
        {
            get
            {
                if (_contentClient == null)
                    _contentClient = new ContentClient(this);
                return _contentClient;
            }
        }

        PageClient _pageClient = null;
        public PageClient PageClient
        {
            get
            {
                if (_pageClient == null)
                    _pageClient = new PageClient(this);
                return _pageClient;
            }
        }

        PageHistoryClient _historyClient = null;
        public PageHistoryClient HistoryClient
        {
            get
            {
                if (_historyClient == null)
                    _historyClient = new PageHistoryClient(this);
                return _historyClient;
            }
        }

        public string DataSource { get; set; }

        public static string CurrentVersion { get => "2"; }

        public static ILoggerFactory MyLoggerFactory {
            get
            {
                return LoggerFactory.Create(builder => {
                    builder.AddFilter("Microsoft", LogLevel.Warning)                       
                        .AddFilter("System", LogLevel.Warning)
                        .AddFilter("MemoriaNote", LogLevel.Debug)     
                        .AddSerilog(Log.Logger);
                    }
                );
            }
        }

        public static string GenerateID(string title)
        {
            return title.CalculateHash();
        }
    }
}
