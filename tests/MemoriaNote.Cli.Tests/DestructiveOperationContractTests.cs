using MemoriaNote.Cli.Tests.Infrastructure;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies destructive-operation options remain limited to applicable commands.</summary>
[TestFixture]
[Category("CliIntegration")]
[NonParallelizable]
public sealed class DestructiveOperationContractTests
{
    /// <summary>Verifies dry-run and force are exposed only where their contracts apply.</summary>
    [Test]
    public async Task Help_ExposesSafetyOptionsOnlyOnApplicableCommands()
    {
        using var harness = new CliInProcessHarness();
        var contracts = new[]
        {
            Contract(new[] { "create" }),
            Contract(new[] { "use" }),
            Contract(new[] { "new" }),
            Contract(new[] { "edit" }),
            Contract(new[] { "rename" }),
            Contract(new[] { "delete" }, dryRun: true, force: true),
            Contract(new[] { "notebooks", "backup" }),
            Contract(new[] { "notebooks", "restore" }, dryRun: true),
            Contract(new[] { "notebooks", "import" }, dryRun: true),
            Contract(new[] { "notebooks", "export" }),
            Contract(new[] { "notebooks", "metadata" }),
            Contract(new[] { "config", "editor", "setup" }),
            Contract(new[] { "config", "editor", "unset" })
        };

        foreach (var contract in contracts)
        {
            var arguments = contract.CommandPath.Append("--help").ToArray();
            var result = await harness.RunAsync(arguments);
            var command = string.Join(' ', contract.CommandPath);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.ExitCode, Is.Zero, command);
                Assert.That(result.StandardError, Is.Empty, command);
                Assert.That(
                    result.StandardOutput.Contains("--dry-run", StringComparison.Ordinal),
                    Is.EqualTo(contract.DryRun),
                    command);
                Assert.That(
                    result.StandardOutput.Contains("--force", StringComparison.Ordinal),
                    Is.EqualTo(contract.Force),
                    command);
            }
        }
    }

    static SafetyOptionContract Contract(
        string[] commandPath,
        bool dryRun = false,
        bool force = false)
    {
        return new SafetyOptionContract(commandPath, dryRun, force);
    }

    sealed record SafetyOptionContract(
        string[] CommandPath,
        bool DryRun,
        bool Force);
}
