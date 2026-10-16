using System.Text;
using MemoriaNote.Cli.Tests.Infrastructure;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies the public completion command and its read-only contract.</summary>
[TestFixture]
[Category("CliIntegration")]
[NonParallelizable]
public sealed class CompletionIntegrationTests
{
    /// <summary>Verifies completion writes only a deterministic zsh script.</summary>
    [Test]
    public async Task CompletionZsh_WritesDeterministicScriptToStandardOutput()
    {
        using var harness = new CliInProcessHarness();

        var first = await harness.RunAsync("completion", "zsh");
        var second = await harness.RunAsync("completion", "zsh");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.ExitCode, Is.Zero);
            Assert.That(first.StandardError, Is.Empty);
            Assert.That(first.StandardOutput, Is.EqualTo(second.StandardOutput));
            Assert.That(first.StandardOutput, Does.StartWith("#compdef mn\n"));
            Assert.That(first.StandardOutput, Does.EndWith("\n"));
            Assert.That(first.StandardOutput, Does.Not.Contain("\r"));
        }
    }

    /// <summary>Verifies completion does not initialize or modify runtime state.</summary>
    [Test]
    public async Task CompletionZsh_DoesNotCreatePersistentOrTemporaryState()
    {
        using var harness = new CliInProcessHarness();
        var before = EnumerateRelativePaths(harness.TestRoot);

        var result = await harness.RunAsync("completion", "zsh");
        var after = EnumerateRelativePaths(harness.TestRoot);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(after, Is.EqualTo(before));
            Assert.That(Directory.Exists(harness.ApplicationDataDirectory), Is.False);
            Assert.That(File.Exists(harness.UserConfigurationPath), Is.False);
            Assert.That(
                Directory.EnumerateFileSystemEntries(harness.WorkingDirectory),
                Is.Empty);
            Assert.That(
                Directory.EnumerateFileSystemEntries(harness.TemporaryDirectory),
                Is.Empty);
        }
    }

    /// <summary>Verifies unsupported shell names follow the parser error contract.</summary>
    [Test]
    public async Task Completion_WithUnsupportedShell_ReturnsValidationError()
    {
        using var harness = new CliInProcessHarness();

        var result = await harness.RunAsync("completion", "fish");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.StartWith("Error:"));
            Assert.That(result.StandardError, Does.Contain("fish"));
        }
    }

    /// <summary>Verifies process output is UTF-8 without BOM and uses LF.</summary>
    [Test]
    [Category("Process")]
    public async Task CompletionZsh_ProcessOutputUsesUtf8WithoutBomAndLf()
    {
        using var harness = new CliProcessHarness();

        var result = await harness.RunAsync("completion", "zsh");
        var bytes = result.StandardOutputBytes;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(bytes, Is.Not.Empty);
            Assert.That(bytes.Take(3), Is.Not.EqualTo(Encoding.UTF8.GetPreamble()));
            Assert.That(bytes, Does.Not.Contain((byte)'\r'));
            Assert.That(bytes[^1], Is.EqualTo((byte)'\n'));
            Assert.That(
                Encoding.UTF8.GetString(bytes),
                Does.StartWith("#compdef mn\n"));
        }
    }

    static string[] EnumerateRelativePaths(string root)
    {
        return Directory.EnumerateFileSystemEntries(
                root,
                "*",
                SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }
}
