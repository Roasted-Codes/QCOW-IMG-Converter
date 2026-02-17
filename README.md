####WARNING - I don't even know if this works######

# qemu-img GUI (.qcow2 <-> .img)

Cross-platform desktop GUI for converting between `.qcow2` and raw `.img` using `qemu-img`.

## Repository layout
- `ConverterApp.csproj` — Avalonia cross-platform GUI (Windows/macOS/Linux).
- `QemuImgWinForms.csproj` — Windows-only WinForms GUI.
- `Xemu-HDD-Converter.sln` — Solution file containing both projects.
- `dist/` — Helper scripts to install `qemu-img` on macOS and Linux.

## Requirements
- .NET 8 SDK to build.
- `qemu-img` available on PATH or placed next to the published app.

## Run locally (dev)
- Avalonia (cross-platform): `dotnet restore && dotnet run`
- Windows WinForms: `dotnet run --project QemuImgWinForms.csproj`

## Publish self-contained builds
Replace the runtime identifier (`-r`) with your target:
```bash
dotnet publish ConverterApp.csproj -c Release -r win-x64 --self-contained true
dotnet publish ConverterApp.csproj -c Release -r osx-x64 --self-contained true
dotnet publish ConverterApp.csproj -c Release -r osx-arm64 --self-contained true
dotnet publish ConverterApp.csproj -c Release -r linux-x64 --self-contained true

# Windows-only WinForms variant
dotnet publish QemuImgWinForms.csproj -c Release -r win-x64 --self-contained true
```
Outputs land in `bin/Release/net8.0/<rid>/publish/`. Add `qemu-img` (and required libs) beside the app or keep it on PATH.

## Installing qemu-img
- **Windows:** Download from [qemu.org](https://www.qemu.org/download/#windows) or install via MSYS2.
- **macOS:** `brew install qemu` (see `dist/get-qemu-img-macos.sh`)
- **Linux:** `sudo apt install qemu-utils` / `sudo dnf install qemu-img` / `sudo pacman -S qemu-img` (see `dist/get-qemu-img-linux.sh`)

## Usage
1. Launch the GUI.
2. Select input file (`.qcow2`, `.img`, or `.raw`).
3. Choose target format (.img/raw or .qcow2); output path auto-fills next to the input.
4. Set `qemu-img` path if not already found.
5. Click **Convert**; watch progress/log; **Cancel** if needed.

## Notes
- Source format is inferred from the input extension (`-f qcow2` for `.qcow2`, `-f raw` for `.img`/`.raw`).
- Progress is parsed from `qemu-img -p` output.
