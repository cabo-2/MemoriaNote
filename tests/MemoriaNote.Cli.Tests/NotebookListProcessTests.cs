using MemoriaNote.Cli.Tests.Infrastructure;
using MemoriaNote.Models;
using MemoriaNote.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies workspace notebook listing through the CLI process boundary.</summary>
[TestFixture]
[Category("Process")]
public sealed class NotebookListProcessTests
{
    /// <summary>Verifies the nested command and long option are discoverable.</summary>
    [Test]
    public async Task Help_DescribesNotebookListing()
    {
        using var harness = new CliProcessHarness();

        var rootHelp = await harness.RunAsync("--help");
        var groupHelp = await harness.RunAsync("notebooks", "--help");
        var listHelp = await harness.RunAsync("notebooks", "list", "--help");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rootHelp.ExitCode, Is.Zero);
            Assert.That(rootHelp.StandardOutput, Does.Contain("notebooks"));
            Assert.That(groupHelp.ExitCode, Is.Zero);
            Assert.That(groupHelp.StandardOutput, Does.Contain("list"));
            Assert.That(listHelp.ExitCode, Is.Zero);
            Assert.That(listHelp.StandardOutput, Does.Contain(
                "Usage: mn notebooks list"));
            Assert.That(listHelp.StandardOutput, Does.Contain("-l|--long"));
            Assert.That(rootHelp.StandardError, Is.Empty);
            Assert.That(groupHelp.StandardError, Is.Empty);
            Assert.That(listHelp.StandardError, Is.Empty);
        }
    }

    /// <summary>
    /// Verifies short output uses ordinal file-name order and exposes exceptional states.
    /// </summary>
    [Test]
    public async Task Execute_ShortFormat_ListsCurrentAndStatusesWithoutChanges()
    {
        using var harness = new CliProcessHarness();
        AssertSucceeded(await harness.RunAsync("create", "project"));
        AssertSucceeded(await harness.RunAsync("create", "Zulu"));
        AssertSucceeded(await harness.RunAsync("create", "alpha"));
        await File.WriteAllTextAsync(
            Path.Combine(harness.WorkingDirectory, "broken.mnote"),
            "not sqlite");
        await UpdateMetadataAsync(
            harness,
            "alpha.mnote",
            new NotebookMetadataPatch().SetReadOnly(true));
        AssertSucceeded(await harness.RunAsync("use", "project"));
        var configurationPath = WorkspaceConfigurationPath(harness);
        var configurationBefore = await File.ReadAllBytesAsync(configurationPath);
        var notebookBytesBefore = await ReadNotebookBytesAsync(harness);

        var result = await harness.RunAsync("notebooks", "list");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(
                GetOutputLines(result.StandardOutput),
                Is.EqualTo(new[]
                {
                    "  Zulu.mnote",
                    "  alpha.mnote [read-only]",
                    "  broken.mnote [invalid]",
                    "* project.mnote"
                }));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(
                await File.ReadAllBytesAsync(configurationPath),
                Is.EqualTo(configurationBefore));
            Assert.That(
                await ReadNotebookBytesAsync(harness),
                Is.EqualTo(notebookBytesBefore));
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
        }
    }

    /// <summary>Verifies long output includes validation, entry kind, and format version.</summary>
    [Test]
    public async Task Execute_LongFormat_WritesNotebookDetails()
    {
        using var harness = new CliProcessHarness();
        AssertSucceeded(await harness.RunAsync("create", "current"));
        AssertSucceeded(await harness.RunAsync("create", "future"));
        AssertSucceeded(await harness.RunAsync("use", "current"));
        await UpdateMetadataAsync(
            harness,
            "future.mnote",
            new NotebookMetadataPatch().SetVersion("2"));

        var result = await harness.RunAsync("notebooks", "list", "--long");
        var lines = GetOutputLines(result.StandardOutput);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(lines, Has.Length.EqualTo(3));
            Assert.That(lines[0], Does.Contain("CURRENT"));
            Assert.That(lines[0], Does.Contain("STATUS"));
            Assert.That(lines[0], Does.Contain("TYPE"));
            Assert.That(lines[0], Does.Contain("FORMAT"));
            Assert.That(lines[0], Does.EndWith("NOTEBOOK"));
            Assert.That(
                lines[1],
                Does.Match(@"^\*\s+ready\s+file\s+1\s+current\.mnote$"));
            Assert.That(
                lines[2],
                Does.Match(@"^\s+unsupported\s+file\s+2\s+future\.mnote$"));
            Assert.That(result.StandardError, Is.Empty);
        }
    }

    /// <summary>Verifies a missing saved selection remains visible without fallback.</summary>
    [Test]
    public async Task Execute_MissingCurrent_AddsSortedDiagnosticEntry()
    {
        using var harness = new CliProcessHarness();
        AssertSucceeded(await harness.RunAsync("create", "available"));
        var configurationPath = WorkspaceConfigurationPath(harness);
        const string configuration =
            "format_version = 1\ncurrent_notebook = \"missing.mnote\"\n";
        await File.WriteAllTextAsync(configurationPath, configuration);

        var result = await harness.RunAsync("notebooks", "list");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(
                GetOutputLines(result.StandardOutput),
                Is.EqualTo(new[]
                {
                    "  available.mnote",
                    "* missing.mnote [missing]"
                }));
            Assert.That(await File.ReadAllTextAsync(configurationPath), Is.EqualTo(configuration));
        }
    }

    /// <summary>Verifies only validly named workspace-root file entries are considered.</summary>
    [Test]
    public async Task Execute_IgnoresSubdirectoriesUppercaseSuffixesAndDirectories()
    {
        using var harness = new CliProcessHarness();
        var nestedDirectory = Path.Combine(harness.WorkingDirectory, "nested");
        Directory.CreateDirectory(nestedDirectory);
        AssertSucceeded(await harness.RunAsync(
            "--workspace",
            nestedDirectory,
            "create",
            "inside"));
        Directory.CreateDirectory(
            Path.Combine(harness.WorkingDirectory, "directory.mnote"));
        File.Copy(
            Path.Combine(nestedDirectory, "inside.mnote"),
            Path.Combine(harness.WorkingDirectory, "uppercase.MNOTE"));

        var shortResult = await harness.RunAsync("notebooks", "list");
        var longResult = await harness.RunAsync("notebooks", "list", "-l");
        var explicitWorkspace = await harness.RunAsync(
            "notebooks",
            "list",
            "--workspace",
            nestedDirectory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(shortResult.ExitCode, Is.Zero, shortResult.StandardError);
            Assert.That(shortResult.StandardOutput, Is.Empty);
            Assert.That(longResult.ExitCode, Is.Zero, longResult.StandardError);
            Assert.That(longResult.StandardOutput, Is.Empty);
            Assert.That(
                GetOutputLines(explicitWorkspace.StandardOutput),
                Is.EqualTo(new[] { "  inside.mnote" }));
            Assert.That(explicitWorkspace.StandardError, Is.Empty);
            Assert.That(File.Exists(WorkspaceConfigurationPath(harness)), Is.False);
        }
    }

    /// <summary>Verifies file symbolic links are listed by workspace alias.</summary>
    [Test]
    public async Task Execute_SymbolicLinks_ReportsReadyAndDanglingAliases()
    {
        using var harness = new CliProcessHarness();
        var externalDirectory = Path.Combine(harness.ApplicationDataRoot, "external");
        Directory.CreateDirectory(externalDirectory);
        AssertSucceeded(await harness.RunAsync(
            "--workspace",
            externalDirectory,
            "create",
            "target"));
        var targetPath = Path.Combine(externalDirectory, "target.mnote");
        var absentTargetPath = Path.Combine(externalDirectory, "absent.mnote");
        var externalSubdirectory = Path.Combine(externalDirectory, "subdirectory");
        Directory.CreateDirectory(externalSubdirectory);
        try
        {
            File.CreateSymbolicLink(
                Path.Combine(harness.WorkingDirectory, "alias.mnote"),
                targetPath);
            File.CreateSymbolicLink(
                Path.Combine(harness.WorkingDirectory, "dangling.mnote"),
                absentTargetPath);
            Directory.CreateSymbolicLink(
                Path.Combine(harness.WorkingDirectory, "linked-directory.mnote"),
                externalSubdirectory);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Ignore("Symbolic links are not available on this platform.");
        }
        AssertSucceeded(await harness.RunAsync("use", "alias"));
        var targetBefore = await File.ReadAllBytesAsync(targetPath);
        var configurationPath = WorkspaceConfigurationPath(harness);
        var configurationBefore = await File.ReadAllBytesAsync(configurationPath);

        var result = await harness.RunAsync("notebooks", "list", "-l");
        var lines = GetOutputLines(result.StandardOutput);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(
                lines.Single(line => line.EndsWith("alias.mnote", StringComparison.Ordinal)),
                Does.Match(@"^\*\s+ready\s+symlink\s+1\s+alias\.mnote$"));
            Assert.That(
                lines.Single(line => line.EndsWith("dangling.mnote", StringComparison.Ordinal)),
                Does.Match(@"^\s+missing\s+symlink\s+-\s+dangling\.mnote$"));
            Assert.That(
                lines.Any(line => line.EndsWith(
                    "linked-directory.mnote",
                    StringComparison.Ordinal)),
                Is.False);
            Assert.That(await File.ReadAllBytesAsync(targetPath), Is.EqualTo(targetBefore));
            Assert.That(File.Exists(absentTargetPath), Is.False);
            Assert.That(
                await File.ReadAllBytesAsync(configurationPath),
                Is.EqualTo(configurationBefore));
        }
    }

    /// <summary>Verifies unusable workspace configuration prevents ambiguous partial output.</summary>
    [Test]
    public async Task Execute_InvalidConfiguration_FailsWithoutChangingSource()
    {
        using var harness = new CliProcessHarness();
        AssertSucceeded(await harness.RunAsync("create", "work"));
        var configurationPath = WorkspaceConfigurationPath(harness);
        const string configuration = "not toml =";
        await File.WriteAllTextAsync(configurationPath, configuration);

        var result = await harness.RunAsync("notebooks", "list");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.StartWith("Error:"));
            Assert.That(await File.ReadAllTextAsync(configurationPath), Is.EqualTo(configuration));
        }
    }

    static async Task UpdateMetadataAsync(
        CliProcessHarness harness,
        string notebookFileName,
        NotebookMetadataPatch patch)
    {
        var factory = new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance);
        var repository = new SqliteNotebookMetadataRepository(factory);
        await repository.UpdateAsync(
            Path.Combine(harness.WorkingDirectory, notebookFileName),
            patch,
            CancellationToken.None);
    }

    static async Task<Dictionary<string, byte[]>> ReadNotebookBytesAsync(
        CliProcessHarness harness)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(
            harness.WorkingDirectory,
            "*.mnote",
            SearchOption.TopDirectoryOnly))
        {
            result.Add(Path.GetFileName(path), await File.ReadAllBytesAsync(path));
        }
        return result;
    }

    static string[] GetOutputLines(string output)
    {
        return output.Split(
            new[] { '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries);
    }

    static string WorkspaceConfigurationPath(CliProcessHarness harness)
    {
        return Path.Combine(
            harness.WorkingDirectory,
            WorkspaceConfigurationStore.FileName);
    }

    static void AssertSucceeded(CliProcessResult result)
    {
        Assert.That(result.ExitCode, Is.Zero, result.StandardError);
    }
}
