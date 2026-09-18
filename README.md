# Jellyfin Whisper Command

Windows WPF utility that lists Jellyfin media, keeps selections across result pages, and executes `whisperjav.exe` with the selected media.

## Configuration

Before starting, copy `appsettings.example.json` as `appsettings.json` next to the executable, then fill in:

- `Jellyfin.BaseUrl`: Jellyfin server address, such as `http://192.168.2.24:8096`
- `Jellyfin.ApiKey`: Jellyfin API key
- `WhisperJav.ExecutablePath`: full path to `whisperjav.exe`; the process runs with this file's directory as its working directory
- `WhisperJav.OutputDir`: directory where transcription subtitles are written
- `WhisperJavTranslate`: configuration for `whisperjav-translate.exe`, including its path, provider, endpoint, API key, model, source and target language, and tone
- `Seconv.ExecutablePath`: the `seconv` executable or command available on `PATH`
- `Seconv.MultipleReplaceRulesFile` and `Seconv.InputFolder`: values passed to Seconv's `--multiple-replace` and `--input-folder` options. Every path returned for a selected Jellyfin item is processed as an individual task: WhisperJav transcribes first, `whisperjav-translate.exe` translates the resulting `.ja.merged.whisperjav.srt` file, and only then does Seconv run 5 times with no `--output-folder`. The resulting subtitle is moved from `InputFolder` into the media's folder. A failed stage skips the remaining stages for that path and processing continues with the next path.

Translation is considered successful only when the process exits with code `0` **and** emits `All subtitles translated: YES`. An `All subtitles translated: NO` result is treated as a failure and does not run Seconv.

Retryable translation failures are persisted beside the executable in `failed-translation-tasks.json`. Use **重试失败翻译 (N)** to retry every queued item without searching for or selecting media. Each item leaves the queue only after translation and Seconv both succeed; it remains queued when a retry fails again.

The remembered media library is written beside the program as `user-settings.json`. It is intentionally not placed in AppData.
Execution output is shown in the "日志" tab and is written to `logs\execution.log` beside the program. The current log is archived daily as `logs\execution.yyyyMMdd.log`, with the 30 most recent archive files retained.
WhisperJav failures are additionally appended to `failed-whisperjav-tasks.log` with the media ID, name, path, and failure reason.

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
