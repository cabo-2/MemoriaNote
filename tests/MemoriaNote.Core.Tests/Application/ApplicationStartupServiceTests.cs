using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Application;

/// <summary>
/// Verifies application startup sequencing without a database or presentation scheduler.
/// </summary>
[TestFixture]
public sealed class ApplicationStartupServiceTests
{
    /// <summary>
    /// Verifies a missing default note is created before migration and workgroup loading.
    /// </summary>
    [Test]
    public async Task StartAsync_MissingDefaultCreatesMigratesLoadsAndComposes()
    {
        var events = new List<string>();
        var workgroup = new Workgroup { Name = "Loaded" };
        var service = new ApplicationStartupService(
            new RecordingMigrator(events),
            new RecordingProbe(events, false),
            new RecordingLoader(events, workgroup));
        var request = CreateRequest("first.db", "second.db");

        var result = await service.StartAsync(request, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                events,
                Is.EqualTo(new[]
                {
                    "exists:default.db",
                    "create:default.db",
                    "migrate:first.db",
                    "migrate:second.db",
                    "load"
                }));
            Assert.That(result.Workgroup, Is.SameAs(workgroup));
            Assert.That(result.ApplicationService, Is.Not.Null);
            Assert.That(result.DefaultNoteCreated, Is.True);
        }
    }

    /// <summary>
    /// Verifies an existing default note is not recreated.
    /// </summary>
    [Test]
    public async Task StartAsync_ExistingDefaultSkipsCreation()
    {
        var events = new List<string>();
        var service = new ApplicationStartupService(
            new RecordingMigrator(events),
            new RecordingProbe(events, true),
            new RecordingLoader(events, new Workgroup()));

        var result = await service.StartAsync(
            CreateRequest("default.db"),
            CancellationToken.None);

        Assert.That(
            events,
            Is.EqualTo(new[]
            {
                "exists:default.db",
                "migrate:default.db",
                "load"
            }));
        Assert.That(result.DefaultNoteCreated, Is.False);
    }

    /// <summary>
    /// Verifies migration failures remain observable and prevent workgroup loading.
    /// </summary>
    [Test]
    public void StartAsync_InfrastructureFailureIsPropagated()
    {
        var events = new List<string>();
        var exception = new IOException("Database unavailable.");
        var service = new ApplicationStartupService(
            new RecordingMigrator(events) { Failure = exception },
            new RecordingProbe(events, true),
            new RecordingLoader(events, new Workgroup()));

        Func<Task> start = () => service.StartAsync(
            CreateRequest("default.db"),
            CancellationToken.None);

        Assert.That(
            start,
            Throws.TypeOf<IOException>().With.Message.EqualTo(exception.Message));
        Assert.That(events, Does.Not.Contain("load"));
    }

    /// <summary>
    /// Verifies cancellation stops startup before workgroup loading.
    /// </summary>
    [Test]
    public void StartAsync_CancellationIsPropagated()
    {
        var events = new List<string>();
        using var cancellation = new CancellationTokenSource();
        var migrator = new RecordingMigrator(events)
        {
            BeforeMigrate = token =>
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            }
        };
        var service = new ApplicationStartupService(
            migrator,
            new RecordingProbe(events, true),
            new RecordingLoader(events, new Workgroup()));

        Func<Task> start = () => service.StartAsync(
            CreateRequest("default.db"),
            cancellation.Token);

        Assert.That(start, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(events, Does.Not.Contain("load"));
    }

    static ApplicationStartupRequest CreateRequest(params string[] dataSources)
    {
        return new ApplicationStartupRequest(
            "note",
            "Notepad",
            "default.db",
            dataSources);
    }

    sealed class RecordingProbe : INoteDataSourceProbe
    {
        readonly List<string> _events;
        readonly bool _exists;

        internal RecordingProbe(List<string> events, bool exists)
        {
            _events = events;
            _exists = exists;
        }

        public bool Exists(string dataSource)
        {
            _events.Add($"exists:{dataSource}");
            return _exists;
        }
    }

    sealed class RecordingLoader : IWorkgroupLoader
    {
        readonly List<string> _events;
        readonly Workgroup _workgroup;

        internal RecordingLoader(List<string> events, Workgroup workgroup)
        {
            _events = events;
            _workgroup = workgroup;
        }

        public Workgroup Load()
        {
            _events.Add("load");
            return _workgroup;
        }
    }

    sealed class RecordingMigrator : INoteMigrator
    {
        readonly List<string> _events;

        internal RecordingMigrator(List<string> events)
        {
            _events = events;
        }

        internal Exception? Failure { get; init; }

        internal Action<CancellationToken>? BeforeMigrate { get; init; }

        public Task<Note> CreateAsync(
            string name,
            string title,
            string dataSource,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            _events.Add($"create:{dataSource}");
            return Task.FromResult(new Note());
        }

        public Task MigrateAsync(string dataSource, CancellationToken token)
        {
            _events.Add($"migrate:{dataSource}");
            BeforeMigrate?.Invoke(token);
            return Failure == null
                ? Task.CompletedTask
                : Task.FromException(Failure);
        }
    }
}
