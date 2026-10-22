using MemoriaNote.Performance;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

return await new PerformanceApplication(Console.Out, Console.Error)
    .RunAsync(args, cancellation.Token);
