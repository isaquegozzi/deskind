param(
    [string]$Device,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. "$PSScriptRoot\lib\DeskInk.Adb.ps1"

if (-not $SkipBuild) {
    & "$PSScriptRoot\build-android.ps1" -Configuration Debug
    if ($LASTEXITCODE -ne 0) {
        throw 'Android build falhou antes da instalação.'
    }
}

$apk = Join-Path $PSScriptRoot '..\apps\android\app\build\outputs\apk\debug\app-debug.apk'
if (-not (Test-Path -LiteralPath $apk)) {
    throw "APK não encontrado em '$apk'."
}

$adb = Resolve-DeskInkAdb
$serial = Resolve-DeskInkDevice -Adb $adb -Device $Device
Invoke-DeskInkAdb -Adb $adb -Device $serial -Arguments @('install', '-r', $apk)
Write-Output "DeskInk instalado em $serial"
