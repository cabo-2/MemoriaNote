using NUnit.Framework;

namespace MemoriaNote.Cli.Tests.Editors;

/// <summary>Verifies temporary-file exchange with an external editor.</summary>
[TestFixture]
public sealed class EditorFileExchangeTests
{
    /// <summary>Verifies that changed text is returned and the exchange file is removed.</summary>
    [Test]
    public async Task EditAsync_ChangedText_ReturnsTextAndCleansFile()
    {
        using var directory = new TemporaryEditorDirectory();
        using var store = new TemporaryFileStore(directory.Path);
        var exchange = new EditorFileExchange(store);
        string? exchangePath = null;

        var result = await exchange.EditAsync(
            new ExternalEditorDocument("Page name.txt", "before"),
            async (path, token) =>
            {
                exchangePath = path;
                await File.WriteAllTextAsync(path, "after", token);
            },
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsChanged, Is.True);
            Assert.That(result.Text, Is.EqualTo("after"));
            Assert.That(exchangePath, Is.Not.Null);
            Assert.That(File.Exists(exchangePath), Is.False);
        }
    }

    /// <summary>Verifies that unchanged text remains a successful no-change result.</summary>
    [Test]
    public async Task EditAsync_UnchangedText_ReturnsUnchangedAndCleansFile()
    {
        using var directory = new TemporaryEditorDirectory();
        using var store = new TemporaryFileStore(directory.Path);
        var exchange = new EditorFileExchange(store);
        string? exchangePath = null;

        var result = await exchange.EditAsync(
            new ExternalEditorDocument("Page name.txt", "same"),
            (path, _) =>
            {
                exchangePath = path;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsChanged, Is.False);
            Assert.That(result.Text, Is.EqualTo("same"));
            Assert.That(File.Exists(exchangePath), Is.False);
        }
    }

    /// <summary>Verifies that deleting the exchange file is treated as no change.</summary>
    [Test]
    public async Task EditAsync_DeletedFile_ReturnsUnchanged()
    {
        using var directory = new TemporaryEditorDirectory();
        using var store = new TemporaryFileStore(directory.Path);
        var exchange = new EditorFileExchange(store);

        var result = await exchange.EditAsync(
            new ExternalEditorDocument("Page name.txt", "same"),
            (path, _) =>
            {
                File.Delete(path);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsChanged, Is.False);
            Assert.That(result.Text, Is.EqualTo("same"));
            Assert.That(Directory.EnumerateFiles(directory.Path), Is.Empty);
        }
    }

    /// <summary>Verifies that process failures still remove the exchange file.</summary>
    [Test]
    public void EditAsync_ProcessFailure_CleansFileAndRethrows()
    {
        using var directory = new TemporaryEditorDirectory();
        using var store = new TemporaryFileStore(directory.Path);
        var exchange = new EditorFileExchange(store);

        Func<Task> edit = () => exchange.EditAsync(
            new ExternalEditorDocument("Page name.txt", "same"),
            (_, _) => throw new InvalidOperationException("process failed"),
            CancellationToken.None);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(edit);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Is.EqualTo("process failed"));
            Assert.That(Directory.EnumerateFiles(directory.Path), Is.Empty);
        }
    }

    /// <summary>Verifies that cancellation still removes the exchange file.</summary>
    [Test]
    public void EditAsync_Canceled_CleansFileAndRethrows()
    {
        using var directory = new TemporaryEditorDirectory();
        using var store = new TemporaryFileStore(directory.Path);
        var exchange = new EditorFileExchange(store);

        Func<Task> edit = () => exchange.EditAsync(
            new ExternalEditorDocument("Page name.txt", "same"),
            (_, token) => Task.FromCanceled(token),
            new CancellationToken(canceled: true));

        Assert.CatchAsync<OperationCanceledException>(edit);
        Assert.That(Directory.EnumerateFiles(directory.Path), Is.Empty);
    }

    sealed class TemporaryEditorDirectory : IDisposable
    {
        internal TemporaryEditorDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"memoria-editor-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
