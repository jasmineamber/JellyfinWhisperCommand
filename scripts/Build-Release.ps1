$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'JellyfinWhisperCommand.csproj'

$runningApp = @(Get-Process -Name 'JellyfinWhisperCommand' -ErrorAction SilentlyContinue)
if ($runningApp.Count -gt 0) {
    $runningApp | Stop-Process -Force
    Write-Host "Closed $($runningApp.Count) running JellyfinWhisperCommand process(es)."
}

& dotnet build $projectFile --configuration Release
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Release build: $(Join-Path $projectRoot 'bin\Release\net8.0-windows\JellyfinWhisperCommand.exe')"
