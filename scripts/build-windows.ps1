param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnet) {
    throw '.NET SDK não foi encontrado no PATH.'
}
$sdks = @(& $dotnet.Source --list-sdks)
if ($sdks.Count -eq 0) {
    throw 'O runtime .NET existe, mas nenhum .NET SDK está instalado.'
}

$solution = Join-Path $PSScriptRoot '..\apps\windows\DeskInk.sln'
& $dotnet.Source build $solution --configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    throw 'Windows build falhou.'
}
