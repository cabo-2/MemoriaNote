using MemoriaNote.Cli.Tests.Infrastructure;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies the isolated in-process CLI test harness.</summary>
[TestFixture]
[Category("CliIntegration")]
[NonParallelizable]
public sealed class CliInProcessHarnessTests
{
    static readonly string[] IsolatedEnvironmentVariableNames =
    {
        "APPDATA",
        "XDG_CONFIG_HOME",
        "MEMORIA_NOTE_APPLICATION_DATA_DIRECTORY",
        "TMPDIR",
        "TMP",
        "TEMP"
    };

    /// <summary>Verifies the production entry point and composition are used.</summary>
    [Test]
    public async Task RunAsync_UsesProductionCompositionAndCapturesTextOutput()
    {
        using var harness = new CliInProcessHarness();

        var result = await harness.RunAsync("status");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(
                result.StandardOutput,
                Does.Contain($"Workspace: {harness.WorkingDirectory}"));
            Assert.That(result.StandardOutput, Does.Contain("Status:    root"));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(
                Directory.Exists(Path.Combine(
                    harness.TemporaryDirectory,
                    ApplicationPaths.ApplicationName)),
                Is.True);
        }
    }

    /// <summary>Verifies text standard input reaches a production command.</summary>
    [Test]
    public async Task RunAsync_WithTextInput_ProvidesCompleteInputToCommand()
    {
        using var harness = new CliInProcessHarness();
        AssertSucceeded(await harness.RunAsync("create", "stdin"));
        AssertSucceeded(await harness.RunAsync("use", "stdin"));
        const string input = "first\r\nsecond\tvalue\n";

        var updated = await harness.RunAsync(
            new[]
            {
                "notebooks",
                "metadata",
                "--description",
                "-"
            },
            input);
        var displayed = await harness.RunAsync("notebooks", "metadata");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated.ExitCode, Is.Zero, updated.StandardError);
            Assert.That(updated.StandardOutput, Does.Contain("field=description"));
            Assert.That(
                displayed.StandardOutput,
                Does.Contain("Description: first\\r\\nsecond\\tvalue"));
            Assert.That(displayed.StandardError, Is.Empty);
        }
    }

    /// <summary>Verifies validation failures restore all process-global state.</summary>
    [Test]
    public async Task RunAsync_AfterValidationFailure_RestoresGlobalState()
    {
        const string CustomVariable = "MEMORIA_NOTE_NCLI_640B_TEST";
        const string UnsetVariable = "MEMORIA_NOTE_NCLI_640B_UNSET_TEST";
        var originalCustomValue = Environment.GetEnvironmentVariable(CustomVariable);
        var originalUnsetValue = Environment.GetEnvironmentVariable(UnsetVariable);
        Environment.SetEnvironmentVariable(CustomVariable, "outside");
        Environment.SetEnvironmentVariable(UnsetVariable, null);
        try
        {
            using var harness = new CliInProcessHarness();
            harness.SetEnvironmentVariable(CustomVariable, "inside");
            harness.SetEnvironmentVariable(UnsetVariable, "temporary");
            var originalState = CaptureGlobalState(CustomVariable, UnsetVariable);

            var result = await harness.RunAsync("create");

            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            AssertGlobalState(originalState);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CustomVariable, originalCustomValue);
            Environment.SetEnvironmentVariable(UnsetVariable, originalUnsetValue);
        }
    }

    /// <summary>Verifies exceptions restore state and release the invocation gate.</summary>
    [Test]
    public async Task RunAsync_AfterException_RestoresStateAndAllowsAnotherInvocation()
    {
        using var harness = new CliInProcessHarness();
        var invalidTemporaryDirectory = Path.Combine(
            harness.TestRoot,
            "not-a-directory");
        await File.WriteAllTextAsync(invalidTemporaryDirectory, "file");
        SetTemporaryEnvironment(harness, invalidTemporaryDirectory);
        var originalState = CaptureGlobalState();

        Assert.That(
            async () => await harness.RunAsync("status"),
            Throws.InstanceOf<IOException>());
        AssertGlobalState(originalState);

        SetTemporaryEnvironment(harness, harness.TemporaryDirectory);
        var recovered = await harness.RunAsync("status");

        Assert.That(recovered.ExitCode, Is.Zero, recovered.StandardError);
        AssertGlobalState(originalState);
    }

    /// <summary>Verifies persistent state is shared only within one test root.</summary>
    [Test]
    public async Task MultipleInvocations_AreRepeatableWithoutCrossingTestRoots()
    {
        using var first = new CliInProcessHarness();
        using var second = new CliInProcessHarness();
        AssertSucceeded(await first.RunAsync("create", "selected"));
        AssertSucceeded(await first.RunAsync("use", "selected"));

        var firstStatus = await first.RunAsync("status");
        var secondStatus = await second.RunAsync("status");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.TestRoot, Is.Not.EqualTo(second.TestRoot));
            Assert.That(firstStatus.StandardOutput, Does.Contain("Status:    ready"));
            Assert.That(firstStatus.StandardOutput, Does.Contain("selected.mnote"));
            Assert.That(secondStatus.StandardOutput, Does.Contain("Status:    root"));
            Assert.That(secondStatus.StandardOutput, Does.Not.Contain("selected.mnote"));
            Assert.That(
                File.Exists(Path.Combine(
                    second.WorkingDirectory,
                    "selected.mnote")),
                Is.False);
        }
    }

    /// <summary>Verifies disposal removes the default test root.</summary>
    [Test]
    public void Dispose_RemovesTestRoot()
    {
        var harness = new CliInProcessHarness();
        var testRoot = harness.TestRoot;

        harness.Dispose();

        Assert.That(Directory.Exists(testRoot), Is.False);
    }

    /// <summary>Verifies in-process execution does not corrupt process execution.</summary>
    [Test]
    public async Task RunAsync_FollowedByProcessSmoke_Succeeds()
    {
        using (var inProcess = new CliInProcessHarness())
        {
            var inProcessResult = await inProcess.RunAsync("status");
            Assert.That(
                inProcessResult.ExitCode,
                Is.Zero,
                inProcessResult.StandardError);
        }

        using var process = new CliProcessHarness();
        var processResult = await process.RunAsync("--help");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(processResult.ExitCode, Is.Zero, processResult.StandardError);
            Assert.That(processResult.StandardOutput, Does.Contain("Usage: mn"));
            Assert.That(processResult.StandardError, Is.Empty);
        }
    }

    static void SetTemporaryEnvironment(
        CliInProcessHarness harness,
        string directory)
    {
        harness.SetEnvironmentVariable("TMPDIR", directory);
        harness.SetEnvironmentVariable("TMP", directory);
        harness.SetEnvironmentVariable("TEMP", directory);
    }

    static GlobalState CaptureGlobalState(params string[] additionalVariables)
    {
        var environment = IsolatedEnvironmentVariableNames
            .Concat(additionalVariables)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(
                name => name,
                Environment.GetEnvironmentVariable,
                StringComparer.Ordinal);
        return new GlobalState(
            Console.In,
            Console.Out,
            Console.Error,
            Directory.GetCurrentDirectory(),
            environment);
    }

    static void AssertGlobalState(GlobalState expected)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Console.In, Is.SameAs(expected.Input));
            Assert.That(Console.Out, Is.SameAs(expected.Output));
            Assert.That(Console.Error, Is.SameAs(expected.Error));
            Assert.That(
                Directory.GetCurrentDirectory(),
                Is.EqualTo(expected.CurrentDirectory));
            foreach (var environmentVariable in expected.Environment)
            {
                Assert.That(
                    Environment.GetEnvironmentVariable(environmentVariable.Key),
                    Is.EqualTo(environmentVariable.Value),
                    environmentVariable.Key);
            }
        }
    }

    static void AssertSucceeded(CliInProcessResult result)
    {
        Assert.That(result.ExitCode, Is.Zero, result.StandardError);
    }

    sealed record GlobalState(
        TextReader Input,
        TextWriter Output,
        TextWriter Error,
        string CurrentDirectory,
        IReadOnlyDictionary<string, string?> Environment);
}
