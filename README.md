# FanucNav

<p align="center">
  <img src="https://raw.githubusercontent.com/lagankapoor1/FanucNav/master/banner.png" alt="FanucNav banner" width="100%" />
</p>

<p align="center">
  <img alt="License" src="https://img.shields.io/badge/License-MIT-green.svg" />
  <img alt="Platform" src="https://img.shields.io/badge/Platform-Windows-0078D6.svg" />
  <img alt="Notepad++" src="https://img.shields.io/badge/Editor-Notepad%2B%2B-4A90E2.svg" />
  <img alt="Language" src="https://img.shields.io/badge/Language-C%23-239120.svg" />
  <img alt="Release" src="https://img.shields.io/github/v/release/lagankapoor1/FanucNav" />
  <img alt="Last Commit" src="https://img.shields.io/github/last-commit/lagankapoor1/FanucNav" />
</p>

<p align="center">
  <strong>Author:</strong> Lagan Kapoor
</p>

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

## Quick start

1. Open Notepad++.
2. Build the plugin output from the included script.
3. Copy the generated plugin folder into the Notepad++ plugins directory.
4. Launch Notepad++ and open the FanucNav panel.
5. Browse the robot backup directory and start navigating call trees and references.

### Typical installation path

```powershell
C:\Program Files\Notepad++\plugins\FanucNav\FanucNav.dll
```

### Portable installation

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

## Troubleshooting

### The plugin does not appear in Notepad++
- Make sure the output is copied into the correct plugins folder.
- Use a 64-bit Notepad++ build.
- Confirm .NET Framework 4.8 is installed.

### Build fails
- Run the script from an elevated PowerShell session if needed.
- Verify the project path and that the build script has not been blocked by execution policy.

### No references show up
- Confirm the selected file is a valid FANUC TP/LS program.
- Check that the backup folder was indexed correctly.

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

## Sponsor

Support future improvements, maintenance, and plugin refinements for FANUC tooling.

## Contributing

Contributions are welcome. If you would like to improve the parser, UI, or documentation:

1. Fork the repository.
2. Create a feature branch.
3. Make a focused change.
4. Open a pull request with a clear description.

## Contributors

- Lagan Kapoor

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE) for details.
