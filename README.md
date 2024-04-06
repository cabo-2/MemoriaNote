# Memoria Note

Memoria Note is a console-based notepad application that leverages the power of databases over traditional file systems for managing text data. Built with C# and compatible with both Windows and Linux, Memoria Note offers a robust solution for those who prefer the simplicity and efficiency of terminal-based applications.

## Features

- **Database-Backed Storage:** Instead of relying on the file system, all text data is stored in an SQLite database.
- **Cross-Platform:** Runs on both Windows and Linux.
- **[Terminal.Gui](https://github.com/gui-cs/Terminal.Gui) Interface:** Offers a modern terminal UI experience for managing your notes.
- **Comprehensive CLI:** A wide range of commands for managing configuration, editing, exporting, importing, finding, listing, and creating text data.
- **Shell Integration:** Supports zsh autocomplete for quickly finding and managing your notes.

## Installation

### Get the code

```bash
git clone "https://github.com/cabo-2/MemoriaNote.git"
cd MemoriaNote
```

### Build on Windows 

```pwsh
cd cli
dotnet build
dotnet publish -c Release -r win-x64 --self-contained
```

```pwsh
cp -r bin\Release\net6.0\win-x64\publish C:\\path\to\dir
```

### Build on Linux (Here for WSL)

```bash
cd cli
dotnet build
dotnet publish -c Release -r linux-x64 --self-contained
```

```bash
cp -r bin/Release/net6.0/linux-x64/publish /path/to/dir
```

#### Supports zsh autocomplete

**Requires administrator privileges**

```bash
sudo cp ../misc/zsh-completion/_mn /usr/share/zsh/site-functions/
```

## Usage

After installing Memoria Note, you can perform various operations through the command line:

```bash
mn --help
```

This command displays a help message outlining all available commands and options:

```
Memoria Note CLI - A simple, .NET Terminal.Gui based, Text viewer and editor

Usage: mn [command] [options]

Options:
  --help  Show help information.

Commands:
  config  Manage configuration options
  edit    Edit and manage text commands
  export  Export text files
  find    Find and browse text commands
  import  Import text files
  list    List text
  new     Create text command
  work    List, change and manage note options

Run 'mn [command] --help' for more information about a command.
```

### Examples

Finding text data by a keyword:

```bash
mn find foo*
```

This command would list all text data that matches the keyword, supporting autocomplete for faster navigation.
