namespace MemoriaNote.DatasetPreparation;

internal static class Program
{
    static async Task<int> Main(string[] arguments)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArguments) =>
        {
            eventArguments.Cancel = true;
            cancellation.Cancel();
        };
        try
        {
            return await new DatasetApplication(Console.Out)
                .RunAsync(arguments, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("Dataset preparation was cancelled.");
            return 130;
        }
    }
}
