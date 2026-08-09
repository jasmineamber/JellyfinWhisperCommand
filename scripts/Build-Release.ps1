$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'JellyfinWhisperCommand.csproj'

& dotnet build $projectFile --configuration Release
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Release build: $(Join-Path $projectRoot 'bin\Release\net8.0-windows\JellyfinWhisperCommand.exe')"
