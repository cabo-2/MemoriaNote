using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;
using MemoriaNote.Integration.Validation;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Integration.Validation;

/// <summary>Verifies an unrelated assembly can restore and execute the exact Core package.</summary>
[TestFixture]
[Category("Process")]
[NonParallelizable]
public sealed class ExternalPackageConsumerTests
{
#if DEBUG
    private const string BuildConfiguration = "Debug";
#else
    private const string BuildConfiguration = "Release";
#endif

    /// <summary>Builds a package, restores it in a temporary project, and runs a small workflow.</summary>
    [Test]
    public async Task ExactPackage_RestoresAndRunsWithoutFriendAccess()
    {
        var descriptor = CoreIntegrationContract.GetDescriptor();
        var repositoryRoot = CoreIntegrationContractTests.FindRepositoryRoot();
        using var directory = new TemporaryDirectory();
        var feed = directory.GetPath("feed");
        var consumer = directory.GetPath("consumer");
        Directory.CreateDirectory(feed);
        Directory.CreateDirectory(consumer);

        await RunAsync(
            repositoryRoot,
            "dotnet",
            "pack",
            Path.Combine(repositoryRoot, "core", "MemoriaNote.Core.csproj"),
            "--no-build",
            "--configuration",
            BuildConfiguration,
            "--output",
            feed,
            $"-p:MemoriaNoteFullCommitId={descriptor.SourceCommit}",
            $"-p:MemoriaNotePackageVersion={descriptor.PackageVersion}");

        var packagePath = Directory.GetFiles(feed, "*.nupkg").Single();
        AssertPackage(packagePath, descriptor);
        await File.WriteAllTextAsync(
            Path.Combine(consumer, "ExternalConsumer.csproj"),
            ProjectSource(descriptor.PackageVersion));
        await File.WriteAllTextAsync(
            Path.Combine(consumer, "NuGet.Config"),
            NuGetConfigSource(feed));
        await File.WriteAllTextAsync(
            Path.Combine(consumer, "Program.cs"),
            ProgramSource());

        await RunAsync(
            consumer,
            "dotnet",
            "restore",
            "ExternalConsumer.csproj",
            "--configfile",
            "NuGet.Config",
            "--ignore-failed-sources");
        var execution = await RunAsync(
            consumer,
            "dotnet",
            "run",
            "--project",
            "ExternalConsumer.csproj",
            "--no-restore",
            "--",
            directory.GetPath("external.mnote"));

        Assert.That(
            execution.StandardOutput.Trim(),
            Is.EqualTo(
                $"{descriptor.PackageId}|{descriptor.PackageVersion}|" +
                $"{descriptor.SourceCommit}|{descriptor.ContractRevision}"));
    }

    private static void AssertPackage(
        string packagePath,
        CoreIntegrationDescriptor descriptor)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var nuspecEntry = archive.Entries.Single(entry => entry.FullName.EndsWith(
            ".nuspec",
            StringComparison.Ordinal));
        using var stream = nuspecEntry.Open();
        var document = XDocument.Load(stream);
        XNamespace ns = document.Root!.Name.Namespace;
        var metadata = document.Root.Element(ns + "metadata")!;
        var repository = metadata.Element(ns + "repository")!;
        var dependencies = metadata
            .Descendants(ns + "dependency")
            .Select(element => (string?)element.Attribute("id"))
            .ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That((string?)metadata.Element(ns + "id"), Is.EqualTo(descriptor.PackageId));
            Assert.That(
                (string?)metadata.Element(ns + "version"),
                Is.EqualTo(descriptor.PackageVersion));
            Assert.That(
                (string?)repository.Attribute("commit"),
                Is.EqualTo(descriptor.SourceCommit));
            Assert.That(
                (string?)metadata.Element(ns + "tags"),
                Does.Contain("integration-contract-v1"));
            Assert.That(dependencies, Has.None.EqualTo("mn"));
            Assert.That(dependencies, Has.None.Contains("SchemaLab"));
            Assert.That(
                archive.Entries.Select(entry => entry.FullName),
                Has.Some.EqualTo("lib/net10.0/MemoriaNote.Core.dll"));
            Assert.That(
                archive.Entries.Select(entry => entry.FullName),
                Has.None.Contains("mn.dll"));
        }
    }

    private static string ProjectSource(string packageVersion) => $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="MemoriaNote.Core" Version="{{packageVersion}}" />
          </ItemGroup>
        </Project>
        """;

    private static string NuGetConfigSource(string feed) => new XDocument(
        new XElement(
            "configuration",
            new XElement(
                "packageSources",
                new XElement("clear"),
                new XElement(
                    "add",
                    new XAttribute("key", "test-feed"),
                    new XAttribute("value", feed)),
                new XElement(
                    "add",
                    new XAttribute("key", "nuget.org"),
                    new XAttribute("value", "https://api.nuget.org/v3/index.json")))))
        .ToString(SaveOptions.DisableFormatting);

    private static string ProgramSource() => """
        using MemoriaNote.Application;
        using MemoriaNote.Integration.Validation;

        if (args.Length != 1)
            return 10;

        var created = await NotebookApplication.CreateAsync(
            new NotebookCreateRequest(args[0], "External consumer"));
        if (!created.IsSuccess || created.Value is null)
            return 11;

        await using var session = created.Value;
        var page = await session.Pages.CreateAsync(
            new NotebookPageCreateRequest("page", "body", ["tag"]));
        if (!page.IsSuccess)
            return 12;

        var query = await session.Pages.QuerySummariesAsync(
            new NotebookPageQueryRequest(
                "p*",
                NotebookPageQuerySort.Name,
                reverse: false,
                limit: 1));
        if (!query.IsSuccess ||
            query.Value is null ||
            query.Value.Count != 1 ||
            query.Value[0].PageId != page.Value!.PageId)
        {
            return 13;
        }

        var search = await session.Pages.SearchBodyAsync(
            new NotebookBodySearchRequest(
                "body",
                NotebookBodySearchSort.Name,
                limit: 1));
        if (!search.IsSuccess || search.Value is null)
        {
            return 15;
        }
        var searchValue = search.Value;
        if (searchValue.Hits.Count != 1 ||
            searchValue.Hits[0].PageId != page.Value!.PageId ||
            searchValue.Hits[0].Snippet is null ||
            searchValue.Hits[0].Snippet!.Matches.Count != 1 ||
            searchValue.HasMore)
        {
            return 16;
        }

        var validation = NotebookValidation.CreateSession(session);
        var check = await validation.CheckAsync(
        [
            DerivedDataKind.PageTags,
            DerivedDataKind.Fts5Unicode61,
            DerivedDataKind.Fts5Trigram
        ]);
        if (!check.IsSuccess || check.Value is null || !check.Value.IsValid)
            return 14;

        var descriptor = CoreIntegrationContract.GetDescriptor();
        Console.WriteLine(
            $"{descriptor.PackageId}|{descriptor.PackageVersion}|" +
            $"{descriptor.SourceCommit}|{descriptor.ContractRevision}");
        return 0;
        """;

    private static async Task<ProcessResult> RunAsync(
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
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{fileName} did not finish within 120 seconds.");
        }

        var result = new ProcessResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
        Assert.That(
            result.ExitCode,
            Is.Zero,
            () => $"{fileName} failed.{Environment.NewLine}" +
                  $"stdout:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}" +
                  $"stderr:{Environment.NewLine}{result.StandardError}");
        return result;
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "MemoriaNote.PackageConsumer.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        internal string GetPath(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
