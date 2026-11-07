# DynamicIsland

A lightweight Windows desktop media widget inspired by the Dynamic Island. It stays at the top-center of the screen, shows the current media session in a compact pill, and expands into a rich player when hovered.

## Features

- Compact always-on-top pill positioned at the top-center of the primary display
- Hover-to-expand animation with track title, artist, timeline, album art, and controls
- Integrates with Windows Global System Media Transport Controls (GSMTC)
- Supports media sessions from apps such as Spotify, Google Chrome, Microsoft Edge, and Apple Music
- Previous, play/pause, and next-track controls
- Automatically follows display-setting changes and hides from the task switcher
- Release workflow publishes a self-contained single-file `win-x64` executable

## Requirements

- Windows 10 version 19041 or later
- .NET 10 SDK
- A media application that exposes a Windows media session for playback information and controls

## Getting started

Clone the repository and run the application:

```powershell
git clone https://github.com/rudill/DynamicIsland.git
cd DynamicIsland
dotnet run --project DynamicIsland/DynamicIsland.csproj
```

Move the pointer over the pill to expand it. Start media in a supported application to populate the track information and controls.

## Build

Build the solution in Debug or Release mode:

```powershell
dotnet build DynamicIsland.sln -c Release
```

To publish a self-contained single-file executable for 64-bit Windows:

```powershell
dotnet publish DynamicIsland/DynamicIsland.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o ./publish
```

The executable is written to `publish/DynamicIsland.exe`.

## Project structure

- `DynamicIsland/MainWindow.xaml` — widget layout and visual styles
- `DynamicIsland/MainWindow.xaml.cs` — media-session integration, animations, positioning, and controls
- `.github/workflows/dotnet-desktop.yml` — Debug/Release builds and Release artifact publishing

## Contributing

Issues and pull requests are welcome. Please keep changes focused and verify the solution builds with the .NET 10 SDK before submitting a pull request.
