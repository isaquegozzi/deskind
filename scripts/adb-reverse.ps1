param(
    [string]$Device,
    [int]$ControlPort = 27183,
    [int]$InputPort = 27184
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. "$PSScriptRoot\lib\DeskInk.Adb.ps1"

$adb = Resolve-DeskInkAdb
$serial = Resolve-DeskInkDevice -Adb $adb -Device $Device
Set-DeskInkAdbReverse -Adb $adb -Device $serial -ControlPort $ControlPort -InputPort $InputPort
Write-Output "ADB reverse configurado em ${serial}: control=$ControlPort input=$InputPort"
