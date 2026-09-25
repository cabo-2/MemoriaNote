using MemoriaNote.Cli.Tests.Infrastructure;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies persistent notebook selection through the CLI boundary.</summary>
[TestFixture]
public sealed class UseNotebookProcessTests
{
    /// <summary>Verifies use exposes its mutually exclusive inputs.</summary>
    [Test]
    public async Task Help_DescribesNotebookAndRootInputs()
    {
        using var harness = new CliProcessHarness();

        var result = await harness.RunAsync("use", "--help");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StandardOutput, Does.Contain("Usage: mn use"));
            Assert.That(result.StandardOutput, Does.Contain("<notebook>"));
            Assert.That(result.StandardOutput, Does.Contain("--root"));
            Assert.That(result.StandardError, Is.Empty);
        }
    }

    /// <summary>Verifies exactly one selection target is required.</summary>
    [Test]
    public async Task Execute_WithNeitherOrBothTargets_ReturnsValidationFailure()
    {
        using var harness = new CliProcessHarness();

        var neither = await harness.RunAsync("use");
        var both = await harness.RunAsync("use", "work", "--root");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(neither.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(both.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(neither.StandardError, Does.Contain("exactly one"));
            Assert.That(both.StandardError, Does.Contain("exactly one"));
        }
    }

    /// <summary>Verifies selecting a valid notebook writes canonical configuration.</summary>
    [Test]
    public async Task Execute_WithNotebook_SelectsCanonicalLeafName()
    {
        using var harness = new CliProcessHarness();
        Assert.That((await harness.RunAsync("create", "work")).ExitCode, Is.Zero);

        var result = await harness.RunAsync("use", "work");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("work.mnote"));
            Assert.That(
                await File.ReadAllTextAsync(ConfigurationPath(harness)),
                Is.EqualTo(
                    "format_version = 1\ncurrent_notebook = \"work.mnote\"\n"));
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
        }
    }

    /// <summary>Verifies root on an unconfigured workspace remains side-effect free.</summary>
    [Test]
    public async Task Execute_RootWithoutConfiguration_DoesNotCreateFile()
    {
        using var harness = new CliProcessHarness();

        var result = await harness.RunAsync("use", "--root");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("/"));
            Assert.That(File.Exists(ConfigurationPath(harness)), Is.False);
        }
    }

    /// <summary>Verifies root clears a selection and does not delete configuration.</summary>
    [Test]
    public async Task Execute_RootWithSelection_ClearsCurrentNotebook()
    {
        using var harness = new CliProcessHarness();
        Assert.That((await harness.RunAsync("create", "work")).ExitCode, Is.Zero);
        Assert.That((await harness.RunAsync("use", "work")).ExitCode, Is.Zero);

        var result = await harness.RunAsync("use", "--root");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(
                await File.ReadAllTextAsync(ConfigurationPath(harness)),
                Is.EqualTo("format_version = 1\n"));
        }
    }

    /// <summary>Verifies invalid targets do not replace an existing selection.</summary>
    [Test]
    public async Task Execute_MissingOrInvalidNotebook_PreservesSelection()
    {
        using var harness = new CliProcessHarness();
        Assert.That((await harness.RunAsync("create", "current")).ExitCode, Is.Zero);
        Assert.That((await harness.RunAsync("use", "current")).ExitCode, Is.Zero);
        var configurationPath = ConfigurationPath(harness);
        var before = await File.ReadAllBytesAsync(configurationPath);
        await File.WriteAllTextAsync(
            Path.Combine(harness.WorkingDirectory, "invalid.mnote"),
            "not sqlite");

        var missing = await harness.RunAsync("use", "missing");
        var invalid = await harness.RunAsync("use", "invalid");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(missing.ExitCode, Is.EqualTo((int)CliExitCode.NotFound));
            Assert.That(invalid.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(
                await File.ReadAllBytesAsync(configurationPath),
                Is.EqualTo(before));
        }
    }

    /// <summary>Verifies a broken saved target can transition to another notebook or root.</summary>
    [Test]
    public async Task Execute_FromMissingSelection_TransitionsSafely()
    {
        using var harness = new CliProcessHarness();
        Assert.That((await harness.RunAsync("create", "available")).ExitCode, Is.Zero);
        var configurationPath = ConfigurationPath(harness);
        await File.WriteAllTextAsync(
            configurationPath,
            "format_version = 1\ncurrent_notebook = \"missing.mnote\"\n");

        var selectNotebook = await harness.RunAsync("use", "available");
        await File.WriteAllTextAsync(
            configurationPath,
            "format_version = 1\ncurrent_notebook = \"missing.mnote\"\n");
        var selectRoot = await harness.RunAsync("use", "--root");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(selectNotebook.ExitCode, Is.Zero, selectNotebook.StandardError);
            Assert.That(selectRoot.ExitCode, Is.Zero, selectRoot.StandardError);
            Assert.That(
                await File.ReadAllTextAsync(configurationPath),
                Is.EqualTo("format_version = 1\n"));
        }
    }

    /// <summary>Verifies unknown fields are accepted and discarded by an update.</summary>
    [Test]
    public async Task Execute_ConfigurationWithUnknownFields_UpdatesKnownState()
    {
        using var harness = new CliProcessHarness();
        Assert.That((await harness.RunAsync("create", "work")).ExitCode, Is.Zero);
        await File.WriteAllTextAsync(
            ConfigurationPath(harness),
            "format_version = 1\nfuture = true\n");

        var result = await harness.RunAsync("use", "work");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(
                await File.ReadAllTextAsync(ConfigurationPath(harness)),
                Is.EqualTo(
                    "format_version = 1\ncurrent_notebook = \"work.mnote\"\n"));
        }
    }

    /// <summary>Verifies malformed and unsupported configuration is not repaired implicitly.</summary>
    [TestCase("not toml =")]
    [TestCase("format_version = 2\n")]
    public async Task Execute_InvalidConfiguration_PreservesSource(string content)
    {
        using var harness = new CliProcessHarness();
        Assert.That((await harness.RunAsync("create", "work")).ExitCode, Is.Zero);
        await File.WriteAllTextAsync(ConfigurationPath(harness), content);

        var result = await harness.RunAsync("use", "work");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(await File.ReadAllTextAsync(ConfigurationPath(harness)), Is.EqualTo(content));
        }
    }

    /// <summary>Verifies a notebook symlink may be selected by its workspace alias.</summary>
    [Test]
    public async Task Execute_NotebookSymbolicLink_SelectsAlias()
    {
        using var harness = new CliProcessHarness();
        harness.SetEnvironmentVariable("EDITOR", harness.TestEditorExecutablePath);
        harness.SetEnvironmentVariable("MEMORIA_NOTE_TEST_EDITOR_TEXT", "Body");
        var externalDirectory = Path.Combine(harness.ApplicationDataRoot, "external");
        Directory.CreateDirectory(externalDirectory);
        var create = await harness.RunAsync(
            "--workspace",
            externalDirectory,
            "create",
            "target");
        Assert.That(create.ExitCode, Is.Zero, create.StandardError);
        var aliasPath = Path.Combine(harness.WorkingDirectory, "alias.mnote");
        try
        {
            File.CreateSymbolicLink(
                aliasPath,
                Path.Combine(externalDirectory, "target.mnote"));
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Ignore("Symbolic links are not available on this platform.");
        }

        var result = await harness.RunAsync("use", "alias");
        var writeResult = await harness.RunAsync("new", "Page");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(writeResult.ExitCode, Is.Zero, writeResult.StandardError);
            Assert.That(
                await File.ReadAllTextAsync(ConfigurationPath(harness)),
                Does.Contain("current_notebook = \"alias.mnote\""));
        }
    }

    static string ConfigurationPath(CliProcessHarness harness)
    {
        return Path.Combine(
            harness.WorkingDirectory,
            WorkspaceConfigurationStore.FileName);
    }
}
