using System.Reflection;
using MemoriaNote.Models;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies the product assembly version contract.</summary>
[TestFixture]
public sealed class ProductVersionTests
{
    /// <summary>Verifies CLI and Core carry the same product version.</summary>
    [Test]
    public void ProductAssemblies_HaveConsistentVersions()
    {
        var testAssembly = Assembly.GetExecutingAssembly();
        var cliAssembly = typeof(Program).Assembly;
        var coreAssembly = typeof(Notebook).Assembly;
        var expectedProductVersion = MetadataValue(
            testAssembly,
            "ExpectedProductVersion");
        var releaseBuild = bool.Parse(MetadataValue(
            testAssembly,
            "ExpectedReleaseBuild"));
        var expectedAssemblyVersion = Version.Parse(expectedProductVersion + ".0");
        var cliInformationalVersion = InformationalVersion(cliAssembly);
        var assemblyVersion = cliAssembly.GetName().Version ??
            throw new InvalidOperationException("The CLI assembly version is missing.");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(assemblyVersion, Is.EqualTo(expectedAssemblyVersion));
            Assert.That(coreAssembly.GetName().Version, Is.EqualTo(assemblyVersion));
            Assert.That(
                InformationalVersion(coreAssembly),
                Is.EqualTo(cliInformationalVersion));
            Assert.That(
                FileVersion(cliAssembly),
                Is.EqualTo(assemblyVersion.ToString()));
            Assert.That(
                FileVersion(coreAssembly),
                Is.EqualTo(assemblyVersion.ToString()));
            Assert.That(
                cliInformationalVersion,
                releaseBuild
                    ? Is.EqualTo(expectedProductVersion)
                    : Does.Match(
                        $"^{RegexEscape(expectedProductVersion)}" +
                        "-dev\\+[0-9a-f]{7}$"));
        }
    }

    static string MetadataValue(Assembly assembly, string key)
    {
        return assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == key)
            .Value!;
    }

    static string InformationalVersion(Assembly assembly)
    {
        return assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;
    }

    static string FileVersion(Assembly assembly)
    {
        return assembly
            .GetCustomAttribute<AssemblyFileVersionAttribute>()!
            .Version;
    }

    static string RegexEscape(string value) =>
        System.Text.RegularExpressions.Regex.Escape(value);
}
