param(
    [switch]$EnableMouse,
    [switch]$EnablePen,
    [switch]$EnableOverlay,
    [switch]$EnableAll,
    [switch]$EnableLan,
    [ValidateRange(0, 31)][int]$Monitor = 0
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$project = Join-Path $PSScriptRoot '..\apps\windows\DeskInk.WindowsHost\DeskInk.WindowsHost.csproj'
$selectedModes = @($EnableMouse, $EnablePen, $EnableOverlay, $EnableAll).Where({ $_ }).Count
if ($selectedModes -gt 1) { throw 'Escolha apenas EnableMouse, EnablePen ou EnableOverlay.' }
$hostArguments = @('run', '--project', $project, '--')
if ($EnableMouse) {
    $hostArguments += @('--enable-mouse', '--monitor', $Monitor)
}
if ($EnablePen) {
    $hostArguments += @('--enable-pen', '--monitor', $Monitor)
}
if ($EnableOverlay) {
    $hostArguments += @('--enable-overlay', '--monitor', $Monitor)
}
if ($EnableAll) { $hostArguments += @('--enable-all', '--monitor', $Monitor) }
if ($EnableLan) { $hostArguments += '--enable-lan' }
& dotnet @hostArguments
if ($LASTEXITCODE -ne 0) {
    throw 'DeskInk Windows Host encerrou com erro.'
}
