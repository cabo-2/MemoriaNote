using Microsoft.EntityFrameworkCore;

using MemoriaNote.Persistence;

namespace MemoriaNote.Models
{
    /// <summary>
    /// Provides Entity Framework access to one notebook's SQLite database.
    /// </summary>
    public class NotebookDbContext : DbContext
    {
        /// <summary>
        /// Initializes a notebook context that uses its configured database path.
        /// </summary>
        public NotebookDbContext() { }

        /// <summary>
        /// Initializes a notebook context for the specified SQLite database path.
        /// </summary>
        /// <param name="databasePath">The notebook database path.</param>
        public NotebookDbContext(string databasePath)
        {
            DatabasePath = databasePath;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NotebookDbContext"/> class using
        /// externally configured Entity Framework Core options.
        /// </summary>
        /// <param name="options">The options used to configure this context.</param>
        public NotebookDbContext(DbContextOptions<NotebookDbContext> options)
            : base(options)
        {
        }

        /// <summary>Gets or sets the persisted notebook metadata values.</summary>
        public DbSet<NoteKeyValue> Metadata { get; set; }

        /// <summary>Gets or sets the authoritative pages.</summary>
        public DbSet<Page> Pages { get; set; }

        /// <summary>
        /// Gets or sets the Page summary read model maintained by SQLite triggers.
        /// Application writes must treat Pages as authoritative.
        /// </summary>
        public DbSet<PageSummaryRecord> Contents { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (optionsBuilder.IsConfigured)
                return;

            optionsBuilder
                .UseSqlite("Data Source=" + (DatabasePath ?? ":memory:"));
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

            modelBuilder.Entity<PageSummaryRecord>()
                .ToTable("Contents")
                .HasKey(e => e.Uuid);

            modelBuilder.Entity<PageSummaryRecord>()
                .HasIndex(e => new { e.Name, e.Index });
        }

        /// <summary>Gets or sets the SQLite database path used by this context.</summary>
        public string DatabasePath { get; set; }

        /// <summary>Gets the current notebook storage format version.</summary>
        public static string CurrentVersion { get => "1"; }

    }
}
