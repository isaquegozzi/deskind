$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

& "$PSScriptRoot\build-android.ps1" -Configuration Debug
& (Join-Path $PSScriptRoot '..\apps\android\gradlew.bat') --no-daemon testDebugUnitTest -p (Join-Path $PSScriptRoot '..\apps\android')
if ($LASTEXITCODE -ne 0) {
    throw 'Testes Android falharam.'
}

& "$PSScriptRoot\build-windows.ps1" -Configuration Debug
$windowsTests = Join-Path $PSScriptRoot '..\apps\windows\DeskInk.Core.Tests\DeskInk.Core.Tests.csproj'
& dotnet run --project $windowsTests --configuration Debug --no-build
if ($LASTEXITCODE -ne 0) {
    throw 'Testes Windows foundation falharam.'
}

& "$PSScriptRoot\test-input-simulator.ps1"
