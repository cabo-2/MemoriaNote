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
            new ExternalEditorDocument("Page name", "before"),
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
            Assert.That(
                Path.GetFileName(exchangePath),
                Does.Match(@"^Page name_[0-9a-f]{16}\.txt$"));
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

    /// <summary>Verifies that deleting the exchange file is treated as an editor error.</summary>
    [Test]
    public void EditAsync_DeletedFile_ThrowsAndCleansDirectory()
    {
        using var directory = new TemporaryEditorDirectory();
        using var store = new TemporaryFileStore(directory.Path);
        var exchange = new EditorFileExchange(store);

        Func<Task> edit = () => exchange.EditAsync(
            new ExternalEditorDocument("Page name.txt", "same"),
            (path, _) =>
            {
                File.Delete(path);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        var exception = Assert.ThrowsAsync<ExternalEditorExchangeException>(edit);

        Assert.That(
            exception!.Message,
            Is.EqualTo("The external editor removed the temporary document."));
        Assert.That(Directory.EnumerateFiles(directory.Path), Is.Empty);
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

    /// <summary>Verifies invalid UTF-8 is rejected and the exchange file is removed.</summary>
    [Test]
    public void EditAsync_InvalidUtf8_ThrowsAndCleansFile()
    {
        using var directory = new TemporaryEditorDirectory();
        using var store = new TemporaryFileStore(directory.Path);
        var exchange = new EditorFileExchange(store);

        Func<Task> edit = () => exchange.EditAsync(
            new ExternalEditorDocument("Page name", "same"),
            (path, _) => File.WriteAllBytesAsync(path, new byte[] { 0xff }),
            CancellationToken.None);

        var exception = Assert.ThrowsAsync<ExternalEditorExchangeException>(edit);

        Assert.That(exception!.Message, Does.Contain("not valid UTF-8"));
        Assert.That(Directory.EnumerateFiles(directory.Path), Is.Empty);
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
