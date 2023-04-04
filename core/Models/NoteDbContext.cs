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

        public DbSet<NoteKeyValue> Metadata { get; set; }

        public DbSet<Page> Pages { get; set; }

        public DbSet<Content> Contents { get; set; }

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
                .HasAlternateKey(e => e.Uuid);

            modelBuilder.Entity<Page>()
                .HasIndex(e => new { e.Name, e.Index });             

            modelBuilder.Entity<Content>()
                .HasKey(e => e.Uuid);

            modelBuilder.Entity<Content>()
                .HasIndex(e => new { e.Name, e.Index });
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

        public string DataSource { get; set; }

        public static string CurrentVersion { get => "1"; }

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
    }
}
