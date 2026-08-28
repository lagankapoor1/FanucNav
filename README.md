# FanucNav

Author: Lagan Kapoor

FanucNav is a Notepad++ plugin for navigating and understanding FANUC robot backup files written in TP/LS formats. It helps you quickly explore robot programs, trace call relationships, inspect labels and I/O, and work with register and data tables from a large FANUC backup.

## Why this tool exists

When a FANUC backup contains many program files, it becomes hard to follow:
- which program calls which other program
- where a label is defined and used
- which I/O or register values are connected to a routine
- how data like frames, payloads, or motion variables relate to the source

FanucNav brings those relationships into a side panel inside Notepad++ so you can navigate the backup without manually searching each file.

## Features

- Browse and index a FANUC backup folder or archive
- View program call relationships and label references
- Jump to CALL / LBL definitions from the editor or side panel
- Find usages of IO, labels, and calls under the cursor
- Inspect macro and register tables
- Renumber programs and remap label references
- Work from a dockable Notepad++ panel

## Requirements

- Windows
- 64-bit Notepad++
- .NET Framework 4.8
- A FANUC robot backup folder or compatible LS/TP files

## Installation

1. Build the plugin output from source using the included script.
2. Copy the generated plugin folder into your Notepad++ plugins directory.
3. Start Notepad++ and open the plugin menu.
4. Use the FanucNav panel from the plugin menu or shortcuts.

Typical installation path:

```powershell
C:\Program Files\Notepad++\plugins\FanucNav\FanucNav.dll
```

Portable Notepad++:

```powershell
<npp-portable>\plugins\FanucNav\FanucNav.dll
```

## How to use

1. Open a FANUC backup folder from the FanucNav panel.
2. Select a program file from the indexed list.
3. Explore CALLs, labels, I/O, and references in the panel.
4. Double-click a CALL or label to jump to the definition.
5. Use the "Find usages" command from the cursor location to trace references.
6. Use renumbering tools when needed to maintain logical numbering in TP programs.

## Shortcuts

- Ctrl+Alt+F — Show FanucNav panel
- Ctrl+Alt+G — Go to definition
- Ctrl+Alt+R — Find usages under the cursor

## Build from source

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

The build script generates the plugin output for use in Notepad++.

## Project structure

- src — plugin source code
- tests — parser smoke tests
- build.ps1 — build script
- LICENSE — MIT license

## Author

Lagan Kapoor

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE) for details.
