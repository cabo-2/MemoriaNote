var command = args.FirstOrDefault() ?? "success";
switch (command)
{
    case "success":
        Console.WriteLine(args.ElementAtOrDefault(1) ?? "ok");
        return 0;
    case "stderr":
        Console.Error.WriteLine(args.ElementAtOrDefault(1) ?? "diagnostic");
        return 0;
    case "exit":
        return int.Parse(args.ElementAtOrDefault(1) ?? "1", System.Globalization.CultureInfo.InvariantCulture);
    case "sleep":
        await Task.Delay(int.Parse(
            args.ElementAtOrDefault(1) ?? "1000",
            System.Globalization.CultureInfo.InvariantCulture));
        return 0;
    case "large-output":
        var count = int.Parse(
            args.ElementAtOrDefault(1) ?? "100000",
            System.Globalization.CultureInfo.InvariantCulture);
        Console.Write(new string('x', count));
        Console.Error.Write(new string('y', count));
        return 0;
    case "environment":
        Console.WriteLine(Environment.GetEnvironmentVariable(
            args.ElementAtOrDefault(1) ?? "PERFORMANCE_TEST_VALUE"));
        return 0;
    case "write-file":
        await File.WriteAllTextAsync(
            args.ElementAtOrDefault(1) ?? throw new ArgumentException("A path is required."),
            args.ElementAtOrDefault(2) ?? "content");
        return 0;
    default:
        Console.Error.WriteLine($"Unknown test command '{command}'.");
        return 2;
}
