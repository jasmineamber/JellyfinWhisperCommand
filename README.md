# Jellyfin Whisper Command

Windows WPF utility that lists Jellyfin media, keeps selections across result pages, and copies a `whisperjav.exe` command to the clipboard.

## Configuration

Before starting, copy `appsettings.example.json` as `appsettings.json` next to the executable, then fill in:

- `Jellyfin.BaseUrl`: Jellyfin server address, such as `http://192.168.2.24:8096`
- `Jellyfin.ApiKey`: Jellyfin API key
- `WhisperJav.OutputDir`, `TranslateModel`, `TranslateApiKey`, `TranslateEndpoint`: command values

The remembered media library is written beside the program as `user-settings.json`. It is intentionally not placed in AppData.

`appsettings.json` and `user-settings.json` remain beside the executable and are excluded from Git because they can contain credentials and personal selection state.

## Build

The project requires a Windows .NET 8 SDK. No third-party NuGet packages are used.

```powershell
dotnet build .\JellyfinWhisperCommand.csproj
dotnet run --project .\JellyfinWhisperCommand.csproj
```

## Release build

Run the PowerShell script below. It closes a running copy of the application before building to the standard `bin\Release\net8.0-windows` directory.

```powershell
.\scripts\Build-Release.ps1
```
