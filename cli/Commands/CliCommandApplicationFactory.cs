using System;
using System.IO;
using McMaster.Extensions.CommandLineUtils;
using McMaster.Extensions.CommandLineUtils.Conventions;

namespace MemoriaNote.Cli
{
    /// <summary>Builds the production command tree shared by parsing and metadata consumers.</summary>
    internal static class CliCommandApplicationFactory
    {
        internal static CommandLineApplication<Program> Create(
            IConsole console,
            string workingDirectory,
            TextWriter parserOutput)
        {
            if (console == null)
                throw new ArgumentNullException(nameof(console));
            if (workingDirectory == null)
                throw new ArgumentNullException(nameof(workingDirectory));
            if (parserOutput == null)
                throw new ArgumentNullException(nameof(parserOutput));

            var application = new CommandLineApplication<Program>(
                new CliHelpTextGenerator(),
                console,
                workingDirectory);
            application.Conventions.UseDefaultConventions();
            ConfigureParserOutput(application, parserOutput);
            ConfigureValidationErrors(application);
            return application;
        }

        static void ConfigureValidationErrors(CommandLineApplication command)
        {
            command.ValidationErrorHandler = result =>
            {
                command.Error.WriteLine($"Error: {result.ErrorMessage}");
                return (int)CliExitCode.Validation;
            };

            foreach (var child in command.Commands)
                ConfigureValidationErrors(child);
        }

        static void ConfigureParserOutput(
            CommandLineApplication command,
            TextWriter output)
        {
            command.Out = output;
            foreach (var child in command.Commands)
                ConfigureParserOutput(child, output);
        }
    }
}
