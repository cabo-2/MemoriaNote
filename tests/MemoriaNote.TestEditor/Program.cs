using System.Text;

const string EditedTextEnvironmentVariable = "MEMORIA_NOTE_TEST_EDITOR_TEXT";

if (args.Length != 1)
    return 2;

var editedText = Environment.GetEnvironmentVariable(EditedTextEnvironmentVariable);
if (editedText == null)
    return 3;

await File.WriteAllTextAsync(args[0], editedText, Encoding.UTF8);
return 0;
