using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests.Editors;

/// <summary>Verifies external editor process construction and lifetime handling.</summary>
[TestFixture]
public sealed class ExternalEditorProcessRunnerTests
{
    /// <summary>Verifies that executable and document paths are not command-line concatenated.</summary>
    [Test]
    public void CreateStartInfo_PreservesPathsWithSpacesAndQuotes()
    {
        const string executablePath = "/opt/Editor Preview/editor\"build";
        const string documentPath = "/tmp/Page Draft \"one\".txt";

        var startInfo = ExternalEditorProcessRunner.CreateStartInfo(
            executablePath,
            documentPath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(startInfo.FileName, Is.EqualTo(executablePath));
            Assert.That(startInfo.UseShellExecute, Is.False);
            Assert.That(startInfo.Arguments, Is.Empty);
            Assert.That(startInfo.ArgumentList, Is.EqualTo(new[] { documentPath }));
        }
    }

    /// <summary>Verifies that a non-zero editor exit is reported as a failure.</summary>
    [Test]
    public void RunAsync_NonZeroExit_ThrowsAndDisposesProcess()
    {
        var process = new StubEditorProcess(exitCode: 17);
        var runner = CreateRunner(process);

        Func<Task> run = () => runner.RunAsync(
            "editor",
            "/tmp/document",
            CancellationToken.None);

        var exception = Assert.ThrowsAsync<ExternalEditorProcessException>(run);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.ExitCode, Is.EqualTo(17));
            Assert.That(process.DisposeCount, Is.EqualTo(1));
            Assert.That(process.KillEntireProcessTree, Is.Null);
        }
    }

    /// <summary>Verifies that cancellation terminates the editor process tree.</summary>
    [Test]
    public void RunAsync_CanceledWait_KillsProcessTreeAndRethrows()
    {
        using var cancellation = new CancellationTokenSource();
        var process = new StubEditorProcess(exitCode: 0)
        {
            WaitForExitAsyncHandler = token =>
            {
                cancellation.Cancel();
                return Task.FromCanceled(token);
            }
        };
        var runner = CreateRunner(process);

        Func<Task> run = () => runner.RunAsync(
            "editor",
            "/tmp/document",
            cancellation.Token);

        Assert.CatchAsync<OperationCanceledException>(run);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(process.KillEntireProcessTree, Is.True);
            Assert.That(process.DisposeCount, Is.EqualTo(1));
        }
    }

    static ExternalEditorProcessRunner CreateRunner(StubEditorProcess process)
    {
        return new ExternalEditorProcessRunner(
            NullLogger<ExternalEditorProcessRunner>.Instance,
            startInfo =>
            {
                Assert.That(startInfo.FileName, Is.EqualTo("editor"));
                return process;
            });
    }

    sealed class StubEditorProcess : IEditorProcess
    {
        internal StubEditorProcess(int exitCode)
        {
            ExitCode = exitCode;
        }

        internal Func<CancellationToken, Task> WaitForExitAsyncHandler { get; set; } =
            _ => Task.CompletedTask;

        public bool HasExited { get; set; }

        public int ExitCode { get; }

        internal bool? KillEntireProcessTree { get; private set; }

        internal int DisposeCount { get; private set; }

        public Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            return WaitForExitAsyncHandler(cancellationToken);
        }

        public void Kill(bool entireProcessTree)
        {
            KillEntireProcessTree = entireProcessTree;
            HasExited = true;
        }

        public void Dispose()
        {
            DisposeCount++;
        }
    }
}
