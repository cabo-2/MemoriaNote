using System.Diagnostics;
using MemoriaNote.Integration.Validation;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Integration.Validation;

/// <summary>Verifies the independently versioned Core integration provenance.</summary>
[TestFixture]
public sealed class CoreIntegrationContractTests
{
    /// <summary>Verifies runtime provenance identifies the exact source and candidate contract.</summary>
    [Test]
    public async Task Descriptor_ReportsPackageSourceFormatAndCandidates()
    {
        var descriptor = CoreIntegrationContract.GetDescriptor();
        var repositoryRoot = FindRepositoryRoot();
        var currentCommit = await ReadProcessOutputAsync(
            repositoryRoot,
            "git",
            "rev-parse",
            "HEAD");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(CoreIntegrationContract.Revision, Is.EqualTo(3));
            Assert.That(CoreIntegrationContract.BodySearchConformanceCorpusRevision, Is.EqualTo(2));
            Assert.That(CoreIntegrationContract.PackageId, Is.EqualTo("MemoriaNote.Core"));
            Assert.That(descriptor.PackageId, Is.EqualTo(CoreIntegrationContract.PackageId));
            Assert.That(
                descriptor.PackageVersion,
                Does.Match("^0\\.2\\.1-ecli1160e\\.g[0-9a-f]{12}$"));
            Assert.That(descriptor.AssemblyInformationalVersion, Does.StartWith("0.2.1-dev+"));
            Assert.That(descriptor.SourceCommit, Is.EqualTo(currentCommit));
            Assert.That(descriptor.SourceCommit, Does.Match("^[0-9a-f]{40}$"));
            Assert.That(descriptor.TargetFramework, Is.EqualTo(".NETCoreApp,Version=v10.0"));
            Assert.That(descriptor.ContractRevision, Is.EqualTo(3));
            Assert.That(descriptor.NotebookFormatVersion, Is.EqualTo(2));
            Assert.That(descriptor.BodySearchConformanceCorpusRevision, Is.EqualTo(2));
            Assert.That(
                descriptor.DerivedDataCandidates,
                Is.EqualTo(new[]
                {
                    DerivedDataKind.PageTags,
                    DerivedDataKind.Fts5Unicode61,
                    DerivedDataKind.Fts5Trigram
                }));
            Assert.That(
                descriptor.BodySearchCandidates,
                Is.EqualTo(new[]
                {
                    BodySearchCandidate.Scan,
                    BodySearchCandidate.Fts5Unicode61,
                    BodySearchCandidate.Fts5Trigram
                }));
        }
    }

    private static async Task<string> ReadProcessOutputAsync(
        string workingDirectory,
        string fileName,
        params string[] arguments)
    {
        var start = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ??
            throw new InvalidOperationException($"Could not start {fileName}.");
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{fileName} failed: {error}");
        return output.Trim();
    }

    internal static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MemoriaNote.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
