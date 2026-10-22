using MemoriaNote.Performance.Execution;

namespace MemoriaNote.Performance.Tests;

/// <summary>Verifies bounded child-process execution without asserting latency.</summary>
[TestFixture]
public sealed class ProcessExecutorTests
{
    /// <summary>Verifies stdout, stderr, exit status, and environment capture.</summary>
    [Test]
    public async Task ExecuteAsync_SuccessAndEnvironment_CapturesPublicProcessResult()
    {
        using var temporary = new TemporaryDirectory();
        var request = Request(
            temporary.Path,
            ["environment", "PERFORMANCE_TEST_VALUE"],
            new Dictionary<string, string> { ["PERFORMANCE_TEST_VALUE"] = "isolated" });

        var result = await new ProcessExecutor().ExecuteAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(ProcessExecutionStatus.Valid));
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StandardOutputText, Is.EqualTo($"isolated{Environment.NewLine}"));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(result.ElapsedNanoseconds, Is.GreaterThanOrEqualTo(0));
        }
    }

    /// <summary>Verifies both redirected streams drain without a process pipe deadlock.</summary>
    [Test]
    public async Task ExecuteAsync_LargeOutput_DrainsBothStreams()
    {
        using var temporary = new TemporaryDirectory();
        var result = await new ProcessExecutor().ExecuteAsync(
            Request(temporary.Path, ["large-output", "100000"]));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(ProcessExecutionStatus.Valid));
            Assert.That(result.StandardOutput, Has.Length.EqualTo(100000));
            Assert.That(result.StandardError, Has.Length.EqualTo(100000));
        }
    }

    /// <summary>Verifies timeout terminates the child and returns a classified result.</summary>
    [Test]
    public async Task ExecuteAsync_Timeout_IsClassified()
    {
        using var temporary = new TemporaryDirectory();
        var request = Request(temporary.Path, ["sleep", "5000"]) with
        {
            Timeout = TimeSpan.FromMilliseconds(50)
        };

        var result = await new ProcessExecutor().ExecuteAsync(request);

        Assert.That(result.Status, Is.EqualTo(ProcessExecutionStatus.Timeout));
    }

    /// <summary>Verifies a missing executable is returned as a start failure.</summary>
    [Test]
    public async Task ExecuteAsync_MissingExecutable_IsClassified()
    {
        using var temporary = new TemporaryDirectory();
        var request = Request(temporary.Path, []) with
        {
            ExecutablePath = System.IO.Path.Combine(temporary.Path, "missing-executable")
        };

        var result = await new ProcessExecutor().ExecuteAsync(request);

        Assert.That(result.Status, Is.EqualTo(ProcessExecutionStatus.StartFailure));
    }

    static ProcessExecutionRequest Request(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        return new ProcessExecutionRequest(
            TestPaths.TestTargetExecutable,
            arguments,
            null,
            workingDirectory,
            environment ?? new Dictionary<string, string>(),
            TimeSpan.FromSeconds(10));
    }
}
