using MemoriaNote.Performance.CommandLine;

namespace MemoriaNote.Performance.Tests;

/// <summary>Verifies the harness command-line contract.</summary>
[TestFixture]
public sealed class CommandLineParserTests
{
    /// <summary>Verifies an empty invocation displays help.</summary>
    [Test]
    public void Parse_EmptyArguments_ReturnsHelp()
    {
        Assert.That(CommandLineParser.Parse([]), Is.TypeOf<HelpCommand>());
    }

    /// <summary>Verifies scenario selection is repeatable without allowing duplicate scalar options.</summary>
    [Test]
    public void Parse_RunWithRepeatedScenario_PreservesSelection()
    {
        var command = (RunCommand)CommandLineParser.Parse(
        [
            "run",
            "--scenario-manifest", "scenarios.json",
            "--executable", "mn",
            "--artifact-id", "artifact",
            "--product-version", "1.0.0",
            "--source-commit", "abcdef0",
            "--rid", "linux-x64",
            "--deployment-kind", "self-contained",
            "--artifact-input-kind", "publish-directory",
            "--environment-label", "local",
            "--output", "result.json",
            "--scenario", "first",
            "--scenario", "second"
        ]);

        Assert.That(command.Options.ScenarioIds, Is.EqualTo(new[] { "first", "second" }));
    }

    /// <summary>Verifies a required option omission is a usage error.</summary>
    [Test]
    public void Parse_RunWithoutRequiredOptions_IsRejected()
    {
        Assert.That(
            () => CommandLineParser.Parse(["run"]),
            Throws.TypeOf<CommandLineException>()
                .With.Message.Contains("--scenario-manifest"));
    }
}
