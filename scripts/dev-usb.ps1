param(
    [string]$Device,
    [switch]$EnableMouse,
    [switch]$EnablePen,
    [ValidateRange(0, 31)][int]$Monitor = 0
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. "$PSScriptRoot\lib\DeskInk.Adb.ps1"

$adb = Resolve-DeskInkAdb
$serial = Resolve-DeskInkDevice -Adb $adb -Device $Device
$selectedModes = @($EnableMouse, $EnablePen).Where({ $_ }).Count
if ($selectedModes -gt 1) { throw 'Escolha apenas EnableMouse ou EnablePen.' }

& "$PSScriptRoot\build-windows.ps1" -Configuration Debug
& "$PSScriptRoot\build-android.ps1" -Configuration Debug
Set-DeskInkAdbReverse -Adb $adb -Device $serial
& "$PSScriptRoot\install-android.ps1" -Device $serial -SkipBuild

$hostProject = Join-Path $PSScriptRoot '..\apps\windows\DeskInk.WindowsHost\DeskInk.WindowsHost.csproj'
$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = 'dotnet'
$injectionArguments = if ($EnableMouse) {
    " -- --enable-mouse --monitor $Monitor"
} elseif ($EnablePen) {
    " -- --enable-pen --monitor $Monitor"
} else { '' }
$startInfo.Arguments = "run --project `"$hostProject`" --no-build$injectionArguments"
$startInfo.WorkingDirectory = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true

$hostProcess = [System.Diagnostics.Process]::new()
$hostProcess.StartInfo = $startInfo
if (-not $hostProcess.Start()) {
    throw 'Nao foi possivel iniciar o DeskInk Windows Host.'
}
& "$PSScriptRoot\run-android.ps1" -Device $serial

Write-Output "DeskInk M2.5 iniciado em $serial. Host PID=$($hostProcess.Id)."
Write-Output "Toque em 'Connect USB' no tablet. Use scripts/run-windows.ps1 para logs interativos."
