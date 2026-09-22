using System.Text;

const string EditedTextEnvironmentVariable = "MEMORIA_NOTE_TEST_EDITOR_TEXT";

if (args.Length == 0)
    return 2;
if (args.Length > 1 && args[..^1].Any(argument => argument != "--wait"))
    return 4;

var editedText = Environment.GetEnvironmentVariable(EditedTextEnvironmentVariable);
if (editedText == null)
    return 3;

await File.WriteAllTextAsync(args[^1], editedText, Encoding.UTF8);
return 0;
