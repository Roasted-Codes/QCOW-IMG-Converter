# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Cross-platform desktop GUI for converting disk images between `.qcow2` and raw `.img` formats using the `qemu-img` CLI tool. Built with .NET 8 and C#.

## Build & Run Commands

```bash
# Avalonia cross-platform GUI (primary)
dotnet restore && dotnet run

# Windows-only WinForms GUI
dotnet run --project QemuImgWinForms.csproj

# Publish self-contained builds (replace RID as needed)
dotnet publish -c Release -r win-x64 --self-contained true
dotnet publish -c Release -r osx-x64 --self-contained true
dotnet publish -c Release -r osx-arm64 --self-contained true
dotnet publish -c Release -r linux-x64 --self-contained true
```

Published output lands in `bin/Release/net8.0/<rid>/publish/`.

## Architecture

**Two UI projects sharing the same conversion logic:**

- **ConverterApp.csproj** — Avalonia (cross-platform). Uses MVVM with XAML data bindings. Entry: `Program.cs` → `App.axaml` → `MainWindow.axaml` bound to `MainWindowViewModel`.
- **QemuImgWinForms.csproj** — WinForms (Windows-only). Programmatic UI in `MainForm.cs`, entry via `Program.WinForms.cs`. Duplicates conversion logic from the ViewModel.

**Core conversion logic lives in `MainWindowViewModel.cs`:**
- Launches `qemu-img convert -p -f <format> -O <format> <input> <output>` as an async process
- Parses real-time progress via regex on stdout (`([0-9]{1,3}(?:\.[0-9]+)?)%`)
- Format detection: `.qcow2` → qcow2, `.img`/`.raw` → raw, default → qcow2
- Supports cancellation via CancellationTokenSource / process kill
- Custom `DelegateCommand` and `AsyncCommand` ICommand implementations are defined at the bottom of this file

**Key files (all at repo root):**
- `MainWindowViewModel.cs` — ViewModel with all conversion logic, commands, and helpers
- `MainWindow.axaml` / `MainWindow.axaml.cs` — Avalonia UI view
- `MainForm.cs` — WinForms UI (Windows-only)
- `App.axaml` / `App.axaml.cs` — Avalonia app setup (Fluent Light theme)

## Dependencies

- .NET 8 SDK (required to build)
- `qemu-img` binary — must be on PATH or placed next to the published app
- Avalonia UI 11.0.10 (NuGet, for cross-platform project)

## Project Conventions

- C# with nullable reference types enabled, implicit usings
- MVVM pattern for Avalonia; code-behind for WinForms
- No test framework is configured — there are no unit tests
- No linter or formatter configuration files present
- `dist/` contains helper scripts for installing qemu-img on macOS/Linux
