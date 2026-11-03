$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$localDotnet = Join-Path $projectRoot '.dotnet/dotnet.exe'
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools/cli-home'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.nuget/packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

if (Test-Path -LiteralPath $localDotnet) {
    $env:DOTNET_ROOT = Join-Path $projectRoot '.dotnet'
    $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
    & $localDotnet @args
} elseif (Get-Command dotnet -ErrorAction SilentlyContinue) {
    & dotnet @args
} else {
    throw 'Установите .NET 10 SDK. Инструкция приведена в README.md.'
}
exit $LASTEXITCODE
