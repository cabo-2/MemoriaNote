using MemoriaNote.Application;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Application;

/// <summary>Verifies timeout and caller-cancellation classification at the Application boundary.</summary>
[TestFixture]
public sealed class NotebookOperationExecutorTests
{
    /// <summary>Verifies an application deadline returns a stable timeout failure.</summary>
    [Test]
    public async Task ExecuteWithTimeoutAsync_DeadlineExpires_ReturnsTimeoutFailure()
    {
        var result = await NotebookOperationExecutor.ExecuteWithTimeoutAsync(
            async cancellationToken =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return 1;
            },
            "page.search",
            TimeSpan.FromMilliseconds(20),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Failure!.Category, Is.EqualTo(NotebookFailureCategory.Timeout));
            Assert.That(result.Failure.Code, Is.EqualTo("page.search.timeout"));
            Assert.That(result.Value, Is.Zero);
        }
    }

    /// <summary>Verifies caller cancellation is propagated rather than reported as a timeout.</summary>
    [Test]
    public void ExecuteWithTimeoutAsync_CallerCanceled_ThrowsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.That(
            async () => await NotebookOperationExecutor.ExecuteWithTimeoutAsync(
                _ => Task.FromResult(1),
                "page.search",
                TimeSpan.FromSeconds(1),
                cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }
}
