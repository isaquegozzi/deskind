param([string]$Device)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. "$PSScriptRoot\lib\DeskInk.Adb.ps1"

$adb = Resolve-DeskInkAdb
$serial = Resolve-DeskInkDevice -Adb $adb -Device $Device
Write-Output "ADB OK: $serial"
