using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies the shared mapping from CLI failures to stable exit codes.</summary>
[TestFixture]
public sealed class CliErrorMappingTests
{
    /// <summary>Verifies the exit code assigned to each explicit error category.</summary>
    [TestCaseSource(nameof(ErrorKinds))]
    public void Failure_MapsErrorKindToStableExitCode(
        object errorKindValue,
        int expectedExitCode)
    {
        var errorKind = (CliErrorKind)errorKindValue;
        var result = CliCommandResult.Failure(errorKind, "failure");

        Assert.That((int)result.ExitCode, Is.EqualTo(expectedExitCode));
    }

    /// <summary>Verifies storage exception classification.</summary>
    [TestCaseSource(nameof(StorageExceptions))]
    public void Map_StorageExceptionReturnsStorageFailure(Exception exception)
    {
        var result = new CliErrorMapper().Map(exception, false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ErrorKind, Is.EqualTo(CliErrorKind.Storage));
            Assert.That(result.ExitCode, Is.EqualTo(CliExitCode.Storage));
            Assert.That(result.Exception, Is.SameAs(exception));
        }
    }

    /// <summary>Verifies known user-facing exception classification.</summary>
    [TestCaseSource(nameof(KnownFailures))]
    public void Map_KnownFailureReturnsExpectedExitCode(
        Exception exception,
        int expectedExitCode)
    {
        var result = new CliErrorMapper().Map(exception, false);

        Assert.That((int)result.ExitCode, Is.EqualTo(expectedExitCode));
    }

    /// <summary>Verifies that requested cancellation has its conventional exit code.</summary>
    [Test]
    public void Map_RequestedCancellationReturnsCanceledFailure()
    {
        var exception = new OperationCanceledException();

        var result = new CliErrorMapper().Map(exception, true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ErrorKind, Is.EqualTo(CliErrorKind.Canceled));
            Assert.That(result.ExitCode, Is.EqualTo(CliExitCode.Canceled));
        }
    }

    /// <summary>Verifies that programming failures retain the fatal classification.</summary>
    [Test]
    public void Map_UnexpectedExceptionReturnsUnexpectedFailure()
    {
        var result = new CliErrorMapper().Map(
            new InvalidOperationException("broken"),
            false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ErrorKind, Is.EqualTo(CliErrorKind.Unexpected));
            Assert.That(result.ExitCode, Is.EqualTo(CliExitCode.Unexpected));
        }
    }

    /// <summary>Verifies that the command executor writes mapped failures to standard error.</summary>
    [Test]
    public async Task Executor_FailureWritesDiagnosticAndReturnsExitCode()
    {
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();
        var output = new ConsoleCommandOutput(standardOutput, standardError);
        var executor = new CliCommandExecutor(
            output,
            new CliErrorMapper(),
            NullLogger<CliCommandExecutor>.Instance);

        var result = await executor.ExecuteAsync(
            _ => CliCommandResult.Failure(
                CliErrorKind.Validation,
                "invalid input"),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(standardOutput.ToString(), Is.Empty);
            Assert.That(
                standardError.ToString(),
                Is.EqualTo("Error: invalid input" + Environment.NewLine));
        }
    }

    /// <summary>Verifies that command cancellation stops the handler and uses the canceled exit code.</summary>
    [Test]
    public async Task Executor_WhenCanceled_ReturnsCanceledWithoutInvokingHandler()
    {
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();
        var output = new ConsoleCommandOutput(standardOutput, standardError);
        var executor = new CliCommandExecutor(
            output,
            new CliErrorMapper(),
            NullLogger<CliCommandExecutor>.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var invoked = false;

        var result = await executor.ExecuteAsync(
            _ =>
            {
                invoked = true;
                return CliCommandResult.Success();
            },
            cancellation.Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Canceled));
            Assert.That(invoked, Is.False);
            Assert.That(standardOutput.ToString(), Is.Empty);
            Assert.That(
                standardError.ToString(),
                Is.EqualTo("Error: Operation was canceled" + Environment.NewLine));
        }
    }

    private static IEnumerable<Exception> StorageExceptions()
    {
        yield return new IOException("disk unavailable");
        yield return new UnauthorizedAccessException("access denied");
        yield return new StubDbException("database unavailable");
        yield return new ExternalEditorConfigurationException(
            "editor is not configured");
        yield return new ExternalEditorStartException("missing-editor");
        yield return new ExternalEditorProcessException("editor", 17);
        yield return new DbUpdateException(
            "update failed",
            new IOException("disk unavailable"));
        yield return new AggregateException(
            new IOException("disk unavailable"),
            new StubDbException("database unavailable"));
    }

    private static IEnumerable<TestCaseData> ErrorKinds()
    {
        yield return ErrorKind(CliErrorKind.Validation, CliExitCode.Validation);
        yield return ErrorKind(CliErrorKind.NotFound, CliExitCode.NotFound);
        yield return ErrorKind(CliErrorKind.Conflict, CliExitCode.Conflict);
        yield return ErrorKind(CliErrorKind.Storage, CliExitCode.Storage);
        yield return ErrorKind(CliErrorKind.Canceled, CliExitCode.Canceled);
        yield return ErrorKind(CliErrorKind.Unexpected, CliExitCode.Unexpected);
    }

    private static TestCaseData ErrorKind(
        CliErrorKind errorKind,
        CliExitCode exitCode)
    {
        return new TestCaseData(errorKind, (int)exitCode)
            .SetName($"Failure_{errorKind}_Returns{(int)exitCode}");
    }

    private static IEnumerable<object[]> KnownFailures()
    {
        yield return new object[]
        {
            new FileNotFoundException("missing file"),
            (int)CliExitCode.NotFound
        };
        yield return new object[]
        {
            new DirectoryNotFoundException("missing directory"),
            (int)CliExitCode.NotFound
        };
        yield return new object[]
        {
            new InvalidDataException("invalid data"),
            (int)CliExitCode.Validation
        };
        yield return new object[]
        {
            new ConfigurationFormatException("invalid configuration"),
            (int)CliExitCode.Validation
        };
        yield return new object[]
        {
            new WorkspaceConfigurationFormatException("invalid workspace configuration"),
            (int)CliExitCode.Validation
        };
    }

    private sealed class StubDbException : DbException
    {
        internal StubDbException(string message)
            : base(message)
        {
        }
    }
}
